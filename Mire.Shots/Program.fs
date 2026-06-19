module Mire.Shots.Program

open System.IO
open Mire.Core
open Mire.Layout
open Mire.Widgets
open Mire.Agent
open Mire.Shots

let private t = AppTheme.defaultTheme

let private buf text cursor anchor : TextBuffer =
    { Text = text; Cursor = cursor; Anchor = anchor }

// The custom widget from the "build your own widget" tutorial — a sparkline.
let private sparkBars = [| "▁"; "▂"; "▃"; "▄"; "▅"; "▆"; "▇"; "█" |]

let private sparkline (style: Style) (values: float list) : LayoutNode<unit> =
    match values with
    | [] -> Text.text " " style
    | _ ->
        let lo = List.min values
        let hi = List.max values
        let span = hi - lo

        let glyph v =
            if span <= 0.0 then
                sparkBars.[sparkBars.Length / 2]
            else
                let i = int ((v - lo) / span * float (sparkBars.Length - 1) + 0.5)
                sparkBars.[max 0 (min (sparkBars.Length - 1) i)]

        values |> List.map glyph |> String.concat "" |> fun s -> Text.text s style

// Each scene: file name, window title, surface (w, h), and the node to render —
// the real widgets, laid out through the real engine, on the default brand theme.
let private scenes: (string * string * int * int * LayoutNode<unit>) list =
    [ "text",
      "type & status colors",
      42,
      7,
      Stack.vstack
          [ Text.title "Title — primary"
            Text.text "Body text — default foreground." t.fg
            Text.text "Muted — secondary." t.fgMuted
            Text.text "Subtle — quietest tier." t.fgSubtle
            Text.text "Accent — the one emerald moment." t.accent
            Stack.hstack
                [ Text.text "success " t.success
                  Text.text "warning " t.warning
                  Text.text "danger " t.danger
                  Text.text "info" t.info ] ]

      "badges",
      "Badge · KeyHint",
      46,
      2,
      Stack.vstack
          [ Stack.hstack
                [ Badge.badge t.accentStrong "NEW"
                  Text.text "  " t.fg
                  Badge.badge t.selection "12"
                  Text.text "    " t.fg
                  KeyHint.hint t.key t.fgMuted "⏎" "submit"
                  Text.text "   " t.fg
                  KeyHint.hint t.key t.fgMuted "^C" "quit" ]
            Text.text "" t.fg ]

      "panel",
      "Box · Panel",
      40,
      8,
      Stack.vstack
          [ Box.panel "settings" t.border [ Text.text "a child line" t.fg; Text.text "another line" t.fgMuted ]
            Text.text "" t.fg
            Box.box t.border [ Text.text " plain box " t.fg ] ]

      "listview",
      "ListView",
      30,
      7,
      Box.box
          t.border
          [ ListView.view 5 t.selection t.fg 1 [ "alpha"; "bravo (selected)"; "charlie"; "delta"; "echo" ] ]

      "table",
      "Table",
      46,
      6,
      (let rows =
          [ [ "App.fs"; "modified"; "+42" ]; [ "Theme.fs"; "added"; "+10" ]; [ "Old.fs"; "deleted"; "-99" ] ]

       let columns: Column<string list, unit> list =
           [ Table.textColumn "file" (Length.Cells 14) t.fg (fun r -> r.[0])
             Table.textColumn "status" (Length.Cells 12) t.fgMuted (fun r -> r.[1])
             Table.textColumn "delta" (Length.Cells 8) t.accent (fun r -> r.[2]) ]

       Box.box t.border [ Table.view 3 t.fgSubtle t.selection 0 (fun i -> i = 0) columns rows ])

      "input",
      "Input · TextArea",
      44,
      7,
      Stack.vstack
          [ Input.render 40 t.fg t.selection true (buf "hello world" 5 (Some 0))
            Text.text "" t.fg
            Box.box
                t.border
                [ TextArea.renderWrapped
                      40
                      3
                      t.fg
                      t.selection
                      false
                      (buf "This is a longer paragraph that soft-wraps across visual rows." 0 None) ] ]

      "controls",
      "Toggle · ProgressBar · Spinner",
      34,
      8,
      Stack.vstack
          [ Stack.hstack [ Toggle.checkbox t.fg true "done"; Text.text "   " t.fg; Toggle.checkbox t.fg false "todo" ]
            Stack.hstack [ Toggle.switch t.accentStrong t.fgMuted true; Toggle.switch t.accentStrong t.fgMuted false ]
            Text.text "" t.fg
            ProgressBar.view 28 t.accent t.fgSubtle 0.25
            ProgressBar.view 28 t.accent t.fgSubtle 0.6
            ProgressBar.view 28 t.success t.fgSubtle 1.0
            Text.text "" t.fg
            Spinner.labeled t.accent t.fgMuted 0 "working…" ]

      "statusbar",
      "StatusBar",
      54,
      3,
      StatusBar.statusBar
          [ Text.text "NORMAL" t.accent ]
          [ Text.text "main.fs" t.fgMuted ]
          [ Text.text "ln 1, col 1" t.fgSubtle ]

      "tabs",
      "Tabs",
      36,
      3,
      Box.box t.border [ Tabs.strip t.accentStrong t.fgMuted 0 [ "Files"; "Search"; "Git" ] ]

      "modal",
      "Modal",
      54,
      11,
      Modal.modal
          Style.Default
          t.border
          t.title
          44
          8
          "Confirm"
          (Stack.vstack
              [ Text.text "Apply 3 changes to the working tree?" t.fg
                Text.text "" t.fg
                Stack.hstack
                    [ Badge.badge t.accentStrong " Apply "; Text.text "  " t.fg; Badge.badge t.selection " Cancel " ] ])

      "completion",
      "Completion",
      40,
      9,
      Completion.view 40 9 3 1 30 5 t.border t.selection t.fg 1 [ "/help"; "/model (selected)"; "/clear"; "/quit" ]

      "markdown",
      "Markdown",
      52,
      12,
      Markdown.render
          t.markdown
          50
          "# Heading\n\nA paragraph with **bold**, *italic*, and `code`.\n\n- first bullet\n- second bullet\n\n> a block quote"

      "imagepreview",
      "ImagePreview",
      22,
      8,
      ImagePreview.render 20 7 t.border t.fgMuted "logo.png" (Some(640, 480))

      "sparkline",
      "Sparkline (custom widget)",
      28,
      5,
      Box.panel
          "metrics"
          t.border
          [ Stack.hstack [ Text.text "cpu " t.fgMuted; sparkline t.accent [ 2.0; 4.0; 3.0; 6.0; 5.0; 8.0; 7.0; 9.0; 6.0; 4.0 ] ]
            Stack.hstack [ Text.text "mem " t.fgMuted; sparkline t.success [ 5.0; 5.0; 6.0; 6.0; 7.0; 7.0; 8.0; 8.0; 9.0; 9.0 ] ] ]

      "transcript",
      "ChatTranscript (Mire.Agent)",
      60,
      13,
      ChatTranscript.view
          t
          54
          0
          11
          0
          t.border
          t.fgMuted
          [ UserMsg "build the project"
            AssistantMd "Sure — running the build now."
            ToolCall("shell", "dotnet build", Succeeded, "1.1s", "Build succeeded.") ] ]

[<EntryPoint>]
let main argv =
    let outDir =
        if argv.Length > 0 then argv.[0] else "website/src/generated/shots"

    Directory.CreateDirectory outDir |> ignore

    for (name, title, w, h, node) in scenes do
        let svg = Svg.ofNode title w h node
        let path = Path.Combine(outDir, name + ".svg")
        File.WriteAllText(path, svg)
        printfn "  %-14s %2d x %-2d  -> %s" name w h path

    printfn "Wrote %d screenshots to %s" (List.length scenes) outDir
    0
