﻿module Paket.BindingRedirects

open System
open System.Text
open System.Xml
open System.Xml.Linq
open System.IO
open System.Reflection
open Paket.Xml.Linq

/// Represents a binding redirection
type BindingRedirect = 
    { AssemblyName : string
      Version : string
      PublicKeyToken : string
      Culture : string option }

let private bindingNs = "urn:schemas-microsoft-com:asm.v1"

let private ensureAssemblyBinding doc = 
    doc |> ensurePathExists ("/configuration/runtime/assemblyBinding!" + bindingNs)

/// Updates the supplied MSBuild document with the supplied binding redirect.
let internal setRedirect (doc:XDocument) bindingRedirect =
    let createElementWithNs = createElement (Some bindingNs)
    let tryGetElementWithNs = tryGetElement (Some bindingNs)
    let getElementsWithNs = getElements (Some bindingNs)

    let assemblyBinding = ensureAssemblyBinding doc
    let dependentAssembly =
        assemblyBinding
        |> getElementsWithNs "dependentAssembly"
        |> Seq.tryFind(fun dependentAssembly ->
            defaultArg
                (dependentAssembly
                 |> tryGetElementWithNs "assemblyIdentity"
                 |> Option.bind(tryGetAttribute "name")
                 |> Option.map(fun attribute -> attribute.Value = bindingRedirect.AssemblyName))
                false)
        |> function
           | Some dependentAssembly -> dependentAssembly
           | None ->
                let dependentAssembly = createElementWithNs "dependentAssembly" []
                dependentAssembly.Add(createElementWithNs "assemblyIdentity" ([ "name", bindingRedirect.AssemblyName
                                                                                "publicKeyToken", bindingRedirect.PublicKeyToken
                                                                                "culture", defaultArg bindingRedirect.Culture "neutral" ]))
                assemblyBinding.Add(dependentAssembly)
                dependentAssembly
                
    let newRedirect = createElementWithNs "bindingRedirect" [ "oldVersion", sprintf "0.0.0.0-%s" bindingRedirect.Version
                                                              "newVersion", bindingRedirect.Version ]
    match dependentAssembly |> tryGetElementWithNs "bindingRedirect" with
    | Some redirect -> redirect.ReplaceWith(newRedirect)
    | None -> dependentAssembly.Add(newRedirect)
    doc

let internal indentAssemblyBindings config =
    let assemblyBinding = ensureAssemblyBinding config
    
    let sb = StringBuilder()
    let xmlWriterSettings = XmlWriterSettings()
    xmlWriterSettings.Indent <- true 
    using (XmlWriter.Create(sb, xmlWriterSettings)) (fun writer -> 
                                                        let tempAssemblyBindingNode = XElement.Parse(assemblyBinding.ToString())
                                                        tempAssemblyBindingNode.WriteTo writer)
    let parent = assemblyBinding.Parent
    assemblyBinding.Remove()
    printfn "%s" (sb.ToString())
    let newNode = XElement.Parse(sb.ToString(), LoadOptions.PreserveWhitespace)
    parent.Add(newNode)

/// Applies a set of binding redirects to a single configuration file.
let private applyBindingRedirects bindingRedirects (configFilePath:string) =
    let config = 
        try 
            XDocument.Load(configFilePath, LoadOptions.PreserveWhitespace)
        with
        | :? System.Xml.XmlException as ex ->
            Logging.verbosefn "Illegal xml in file: %s" configFilePath
            raise ex
    let config = Seq.fold setRedirect config bindingRedirects
    indentAssemblyBindings config
    config.Save configFilePath

/// Applies a set of binding redirects to all .config files in a specific folder.
let applyBindingRedirectsToFolder rootPath bindingRedirects =
    Directory.GetFiles(rootPath, "*.config", SearchOption.AllDirectories) 
    |> Seq.filter (fun x -> x.EndsWith(Path.DirectorySeparatorChar.ToString() + "web.config", StringComparison.CurrentCultureIgnoreCase) || x.EndsWith(Path.DirectorySeparatorChar.ToString() + "app.config", StringComparison.CurrentCultureIgnoreCase))
    |> Seq.iter (applyBindingRedirects bindingRedirects)

/// Calculates the short form of the public key token for use with binding redirects, if it exists.
let getPublicKeyToken (assembly:Assembly) =
    ("", assembly.GetName().GetPublicKeyToken())
    ||> Array.fold(fun state b -> state + b.ToString("X2"))
    |> function
    | "" -> None
    | token -> Some <| token.ToLower()
