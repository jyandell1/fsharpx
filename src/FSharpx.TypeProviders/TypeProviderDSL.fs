﻿// Starting to implement a DSL on top of ProvidedTypes API
module FSharpx.TypeProviders.DSL

open System
open Samples.FSharp.ProvidedTypes
open System.Reflection
open Microsoft.FSharp.Quotations
open FSharpx.Strings
open System.Xml.Linq
open System.Collections.Generic
open Microsoft.FSharp.Core.CompilerServices

type FilePosition =  
   { Line: int; 
     Column: int;
     FileName: string }

let fileStart fileName = { Line = 1; Column = 1; FileName = fileName }

/// Turns a string into a nice PascalCase identifier
let niceName (s:string) = 
    if s = s.ToUpper() then s else
    // Starting to parse a new segment 
    let rec restart i = seq {
      match s @? i with 
      | EOF -> ()
      | LetterDigit _ & Upper _ -> yield! upperStart i (i + 1)
      | LetterDigit _ -> yield! consume i false (i + 1)
      | _ -> yield! restart (i + 1) }
    // Parsed first upper case letter, continue either all lower or all upper
    and upperStart from i = seq {
      match s @? i with 
      | Upper _ -> yield! consume from true (i + 1) 
      | Lower _ -> yield! consume from false (i + 1) 
      | _ -> yield! restart (i + 1) }
    // Consume are letters of the same kind (either all lower or all upper)
    and consume from takeUpper i = seq {
      match s @? i with
      | Lower _ when not takeUpper -> yield! consume from takeUpper (i + 1)
      | Upper _ when takeUpper -> yield! consume from takeUpper (i + 1)
      | _ -> 
          yield from, i
          yield! restart i }
    
    // Split string into segments and turn them to PascalCase
    seq { for i1, i2 in restart 0 do 
            let sub = s.Substring(i1, i2 - i1) 
            if Seq.forall Char.IsLetterOrDigit sub then
              yield sub.[0].ToString().ToUpper() + sub.ToLower().Substring(1) }
    |> String.concat ""

let hideOldMethods (typeDef:ProvidedTypeDefinition) = 
    typeDef.HideObjectMethods <- true
    typeDef

let inline addXmlDoc xmlDoc (definition: ^a) = 
    (^a : (member AddXmlDoc: string -> unit) (definition,xmlDoc))
    definition

/// Add metadata defining the property's location in the referenced file
let inline addDefinitionLocation (filePosition:FilePosition) (definition: ^a) = 
    if System.String.IsNullOrEmpty filePosition.FileName then definition else
    (^a : (member AddDefinitionLocation: int*int*string -> unit) (definition,filePosition.Line,filePosition.Column,filePosition.FileName))
    definition

let isGenerated (typeDef:ProvidedTypeDefinition) =    
    typeDef.IsErased <- false
    typeDef

let runtimeType<'a> typeName = ProvidedTypeDefinition(typeName |> niceName, Some typeof<'a>)

let eraseType assemblyName rootNamespace typeName toType = 
    ProvidedTypeDefinition(assemblyName, rootNamespace, typeName, Some toType)

let erasedType<'a> assemblyName rootNamespace typeName = 
    eraseType assemblyName rootNamespace typeName typeof<'a>

let convertToGenerated (assemblyFileName:string) (typeDef:ProvidedTypeDefinition) =
    typeDef.ConvertToGenerated(assemblyFileName)
    typeDef

let literalField name (value:'a) =
    ProvidedLiteralField(niceName name, typeof<'a>, value)

let inline (|+>) (typeDef:ProvidedTypeDefinition) memberDefinitionF =
    typeDef.AddMemberDelayed memberDefinitionF
    typeDef

let inline (|++>) (typeDef:ProvidedTypeDefinition) memberDefinitionsF =
    Seq.fold (fun typeDef memberF -> typeDef |+> memberF) typeDef memberDefinitionsF

let inline (|+!>) (typeDef:ProvidedTypeDefinition) memberDef =
    typeDef.AddMember memberDef
    typeDef

let inline (|++!>) (typeDef:ProvidedTypeDefinition) memberDef =
    typeDef.AddMembers (memberDef |> Seq.toList)
    typeDef

let provideProperty name propertyType quotationF =    
    ProvidedProperty(
        propertyName = niceName name, 
        propertyType = propertyType, 
        GetterCode = quotationF)

let addSetter quotationF (providedProperty:ProvidedProperty) =
    providedProperty.SetterCode <- quotationF
    providedProperty

let makePropertyStatic (providedProperty:ProvidedProperty) =
    providedProperty.IsStatic <- true
    providedProperty

let provideMethod name parameters returnType quotationF =
    ProvidedMethod(
        methodName = niceName name, 
        parameters = 
            (parameters
                |> Seq.map (fun (name,t) -> ProvidedParameter(name, t)) 
                |> Seq.toList), 
        returnType = returnType, 
        InvokeCode = quotationF)

let provideConstructor parameters quotationF =
    ProvidedConstructor(
        parameters = 
            (parameters
                |> Seq.map (fun (name,t) -> ProvidedParameter(name, t)) 
                |> Seq.toList), 
        InvokeCode = quotationF)

let makeStatic (providedMethod:ProvidedMethod) =
    providedMethod.IsStaticMethod <- true
    providedMethod

let staticParameter name instantiateFunction (typeDef:ProvidedTypeDefinition) =
    typeDef.DefineStaticParameters(
        parameters = [ProvidedStaticParameter(name, typeof<'a>)], 
        instantiationFunction = (fun typeName parameterValues ->
            match parameterValues with 
            | [| :? 'a as parameterValue |] -> instantiateFunction typeName parameterValue
            | x -> failwithf "unexpected parameter values %A" x))
    typeDef

let staticParameters parameters instantiateFunction (typeDef:ProvidedTypeDefinition) =
    typeDef.DefineStaticParameters(
        parameters = 
            (parameters
                |> Seq.map (fun (name,t,initValue) -> 
                        match initValue with
                        | None   -> ProvidedStaticParameter(name, t)
                        | Some(v:obj) -> ProvidedStaticParameter(name, t, v)) 
                |> Seq.toList), 
        instantiationFunction = instantiateFunction)
    typeDef

open System.IO

let findConfigFile resolutionFolder configFileName =
    if Path.IsPathRooted configFileName then 
        configFileName 
    else 
        Path.Combine(resolutionFolder, configFileName)

let badargs() = failwith "Wrong type or number of arguments"

/// Implements invalidation of schema when the file changes
let watchForChanges (ownerType:TypeProviderForNamespaces) (fileName:string) = 
    if not (fileName.StartsWith("http", StringComparison.InvariantCultureIgnoreCase)) then
      let path = Path.GetDirectoryName(fileName)
      let name = Path.GetFileName(fileName)
      let watcher = new FileSystemWatcher(Filter = name, Path = path)
      watcher.Changed.Add(fun _ ->
        ownerType.Invalidate()) 
      watcher.EnableRaisingEvents <- true   

let seqType ty = typedefof<seq<_>>.MakeGenericType[| ty |]

let optionType ty = typedefof<option<_>>.MakeGenericType[| ty |]

/// Generates a structured parser
let generateStructuredParser thisAssembly rootNamespace typeName (cfg:TypeProviderConfig) ownerType createTypeFromFileNameF createTypeFromSchemaF =
    let missingValue = "@@@missingValue###"
    erasedType<obj> thisAssembly rootNamespace typeName
    |> isGenerated
    |> staticParameters 
          ["FileName" , typeof<string>, Some(missingValue :> obj)  // Parameterize the type by the file to use as a template
           "Schema" , typeof<string>, Some(missingValue :> obj)  ] // Allows to specify inlined schema
          (fun typeName parameterValues ->
                match parameterValues with 
                | [| :? string as fileName; :? string |] when fileName <> missingValue ->        
                    let resolvedFileName = findConfigFile cfg.ResolutionFolder fileName
                    watchForChanges ownerType resolvedFileName
                
                    createTypeFromFileNameF typeName resolvedFileName
                | [| :? string; :? string as schema |] when schema <> missingValue ->        
                    createTypeFromSchemaF typeName schema
                | _ -> failwith "You have to specify a filename or inlined Schema")
    |> convertToGenerated @"C:\temp\FSharpx.TypeProviders.dll"