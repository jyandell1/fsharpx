﻿module FSharpx.TypeProviders.Tests.InfrastructureTests

open NUnit.Framework
open FSharpx
open FsUnit
open FSharpx.TypeProviders.DSL

[<Test>]
let ``Can simplify the type names``() = 
    let (=!=) a b = a |> should equal b

    niceName "" =!= "" 
    niceName "__hello__" =!= "Hello"
    niceName "abc" =!= "Abc"
    niceName "hello_world" =!= "HelloWorld"
    niceName "HelloWorld" =!= "HelloWorld"
    niceName "helloWorld" =!= "HelloWorld"
    niceName "hello123" =!= "Hello123"
    niceName "Hello123" =!= "Hello123"
    niceName "hello!123" =!= "Hello123"
    niceName "HelloWorld123_hello__@__omg" =!= "HelloWorld123HelloOmg"
    niceName "HKEY_CURRENT_USER" =!= "HKEY_CURRENT_USER"

[<Test>]
let ``Can pluralize names``() =
    let check a b = Strings.pluralize a |> should equal b
    check "Author" "Authors"
    check "Authors" "Authors"
    check "Item" "Items"
    check "Items" "Items"

[<Test>]
let ``Can singularize names``() =
    let check a b = Strings.singularize a |> should equal b
    check "Author" "Author"
    check "Authors" "Author"
    check "Item" "Item"
    check "Items" "Item"

open FSharpx.TypeProviders.Inference

[<Test>]
let ``Can infer floats``() = 
    isFloat "42.42" |> should equal true

type G = GeneratedType

[<Test>]
let ``Can generate type``() = 
    let t = G.Ty()
    t.AddOne(7) |> should equal 8
