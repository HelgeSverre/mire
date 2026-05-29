module Mire.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    // Discovers every [<Tests>]-tagged test list in this assembly.
    runTestsInAssemblyWithCLIArgs [] argv
