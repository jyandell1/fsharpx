module FSharpx.TypeProviders.NamespaceProvider

open System.Reflection
open Microsoft.FSharp.Core.CompilerServices
open Samples.FSharp.ProvidedTypes
open System.Text.RegularExpressions
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System.Reflection.Emit

[<TypeProvider>]
type public FSharpxProvider(cfg:TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()

    // create the hosting generated type
    let pt = ProvidedTypeDefinition(Settings.thisAssembly, Settings.rootNamespace, "GeneratedType", None, IsErased = false)

    do

        // add a generated assembly's types as nested types
        pt.AddAssemblyTypesAsNestedTypesDelayed(fun _ -> 
            let dir = cfg.TemporaryFolder
            // vary the assembly name so that the language service and one FSI session can use the TP at the same time
            let asmName = 
                if cfg.IsHostedExecution then
                    "asm_live"
                else
                    "asm"
            let filename = asmName + ".dll"
            let fullPath = System.IO.Path.Combine(dir, filename)
            // generate an assembly with a single type with one method
            let asmB = System.AppDomain.CurrentDomain.DefineDynamicAssembly(AssemblyName(asmName), AssemblyBuilderAccess.Save, dir)
            let modB = asmB.DefineDynamicModule(filename)
            let tyB = modB.DefineType("Ty", TypeAttributes.Public)
            let methB = tyB.DefineMethod("AddOne", MethodAttributes.Public ||| MethodAttributes.NewSlot ||| MethodAttributes.HideBySig, typeof<int>, [|typeof<int>|])
            let ilg = methB.GetILGenerator()
            ilg.Emit(OpCodes.Ldarg_1)
            ilg.Emit(OpCodes.Ldc_I4_1)
            ilg.Emit(OpCodes.Add)
            ilg.Emit(OpCodes.Ret)
            tyB.CreateType() |> ignore
            // output the assembly to file 
            asmB.Save(filename)

            // read the assembly back from the file 
            // this is necessary for the ManifestModule's FullyQualifiedPath to be set properly
            Assembly.LoadFile(fullPath))

    do this.AddNamespace(
        Settings.rootNamespace, 
        [RegexTypeProvider.regexTy
         MiniCsvProvider.csvType cfg
         FilesTypeProvider.typedFileSystem
         XmlTypeProvider.xmlType this cfg
         JsonTypeProvider.jsonType this cfg
         RegistryProvider.typedRegistry
         XamlProvider.xamlType this cfg
         AppSettingsTypeProvider.typedAppSettings cfg
         ExcelProvider.typExcel cfg
         pt])

[<TypeProviderAssembly>]
do ()