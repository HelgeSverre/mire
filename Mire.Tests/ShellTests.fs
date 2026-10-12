/// AgentShell composition: context-aware key routing, scrolling/follow-tail,
/// coherent session/approval state, completion, multiline prompt, approval clicks,
/// and streaming cost/memory bounds.
module Mire.Tests.ShellTests

open Expecto
open Mire.Core
open Mire.Layout
open Mire.Renderer
open Mire.App
open Mire.Agent

let private theme = Mire.Widgets.AppTheme.defaultTheme

let private key (k: Key) (mods: KeyModifiers) : KeyEvent =
    { Key = k
      Text =
        (match k with
         | Char c -> Some c
         | _ -> None)
      Modifiers = mods
      Repeat = false
      EventType = Press }

let private plain k = key k KeyModifiers.None

let private cfg: ShellConfig =
    { AgentShell.config
          theme
          "t"
          "p"
          (fun text m -> AgentShell.addAssistant ("re: " + text) m, Cmd.none)
          (fun ok cmd m -> AgentShell.addAssistant (sprintf "%b %s" ok cmd) m, Cmd.none) with
        Completions =
            fun tok ->
                if tok.Trigger = '/' then
                    [ "help"; "history" ] |> List.filter (fun c -> c.StartsWith tok.Query)
                else
                    [] }

/// A fresh shell model per use — each session owns its `TranscriptCache`, which
/// (like any per-session state) must not be shared across parallel tests.
let private fresh () =
    { fst (AgentShell.init cfg ()) with
        Size = Size.Create(40, 16) }

let private upd msg m = AgentShell.update cfg msg m |> fst
let private press k m = upd (KeyPress(plain k)) m

let private typeText (s: string) m =
    s |> Seq.fold (fun m c -> press (Char(string c)) m) m

let private text (m: ShellModel) = PromptBox.value m.Prompt
let private blocks (m: ShellModel) = Conversation.blocks m.Conversation

/// A conversation tall enough to scroll.
let private tall (m: ShellModel) =
    [ 1..30 ]
    |> List.fold (fun m i -> AgentShell.addAssistant (sprintf "line %d" i) m) m

[<Tests>]
let routingTests =
    testList
        "AgentShell routing"
        [ test "Left moves the caret: abc, Left, X → abXc" {
              let m = (fresh ()) |> typeText "abc" |> press ArrowLeft |> typeText "X"
              Expect.equal (text m) "abXc" "Left reached the editor"
          }
          test "Home/End and word motions reach the editor" {
              let m = (fresh ()) |> typeText "one two" |> press Home |> typeText ">"
              Expect.equal (text m) ">one two" "Home → line start"
              let m = m |> press End |> typeText "<"
              Expect.equal (text m) ">one two<" "End → line end"

              let m =
                  m
                  |> upd (KeyPress(key ArrowLeft { KeyModifiers.None with Ctrl = true }))
                  |> typeText "_"

              Expect.equal (text m) ">one _two<" "Ctrl+Left → word left"
          }
          test "Shift+Left selects; typing replaces the selection" {
              let shiftLeft = KeyPress(key ArrowLeft { KeyModifiers.None with Shift = true })

              let m =
                  (fresh ()) |> typeText "abc" |> upd shiftLeft |> upd shiftLeft |> typeText "Z"

              Expect.equal (text m) "aZ" "selection replaced"
          }
          test "Enter submits; Shift+Enter and Ctrl+J insert a newline instead" {
              let m =
                  (fresh ())
                  |> typeText "a"
                  |> upd (KeyPress(key Enter { KeyModifiers.None with Shift = true }))

              let m =
                  m
                  |> typeText "b"
                  |> upd (KeyPress(key (Char "j") { KeyModifiers.None with Ctrl = true }))

              Expect.equal (text m) "a\nb\n" "two newlines, no submit"
              Expect.isEmpty (blocks m) "nothing submitted"
              let m = m |> typeText "c" |> press Enter
              Expect.equal (blocks m) [ UserMsg "a\nb\nc"; AssistantMd "re: a\nb\nc" ] "Enter submitted"
          }
          test "Up moves between prompt lines, recalling history only from the first line" {
              let m = (fresh ()) |> typeText "old" |> press Enter // history: [old]

              let m =
                  m
                  |> typeText "l1"
                  |> upd (KeyPress(key Enter { KeyModifiers.None with Shift = true }))
                  |> typeText "l2"

              let m = m |> press ArrowUp
              Expect.equal (text m) "l1\nl2" "Up on line 2 moved the caret, not history"
              let m = m |> press ArrowUp
              Expect.equal (text m) "old" "Up on the first line recalls history"
          }
          test "with an approval open, Left/Right toggle it and typing is swallowed" {
              let m =
                  (fresh ())
                  |> AgentShell.requestApproval "rm"
                  |> typeText "x"
                  |> press ArrowRight

              Expect.equal (text m) "" "no typing under the modal"
              Expect.equal (m.Approval |> Option.map (fun a -> a.AcceptFocused)) (Some false) "focus moved to Deny"
              let m = m |> press Enter
              Expect.equal (blocks m) [ AssistantMd "false rm" ] "Enter confirmed the focused (Deny) button"
          }
          test "clicking an approval button resolves it" {
              let m = (fresh ()) |> AgentShell.requestApproval "ls"

              let click =
                  AgentShell.onMouseRegion
                      (Some ApprovalModal.acceptRegion)
                      { X = 0
                        Y = 0
                        Button = MouseButton.Left
                        Modifiers = KeyModifiers.None
                        Pressed = true
                        Moved = false }

              match click with
              | Some msg ->
                  let m = upd msg m
                  Expect.equal (blocks m) [ AssistantMd "true ls" ] "accepted by click"
              | None -> failtest "a press on the accept region should map to a message"
          }
          test "the rendered approval buttons are hit-testable regions" {
              let m = (fresh ()) |> AgentShell.requestApproval "ls"
              let node = AgentShell.view cfg m
              let laid = Layout.measure (Rect.Create(0, 0, 40, 16)) node
              let ids = Layout.collectRegions laid |> List.map fst
              Expect.contains ids ApprovalModal.acceptRegion "accept button region present"
          } ]

[<Tests>]
let completionTests =
    testList
        "AgentShell completion"
        [ test "typing a trigger opens candidates; Down + Tab accepts the selection" {
              let m = (fresh ()) |> typeText "/h"

              Expect.equal
                  (AgentShell.activeCompletion cfg m |> Option.map snd)
                  (Some [ "help"; "history" ])
                  "popup open"

              let m = m |> press ArrowDown |> press Tab
              Expect.equal (text m) "/history " "second candidate accepted"
              Expect.isNone (AgentShell.activeCompletion cfg m) "popup closed after accepting"
          }
          test "Enter accepts a completion instead of submitting" {
              let m = (fresh ()) |> typeText "/he" |> press Enter
              Expect.equal (text m) "/help " "accepted"
              Expect.isEmpty (blocks m) "not submitted"
          }
          test "Esc dismisses the popup until the next edit" {
              let m = (fresh ()) |> typeText "/h" |> press Escape
              Expect.isNone (AgentShell.activeCompletion cfg m) "dismissed"
              let m = m |> typeText "e"
              Expect.isSome (AgentShell.activeCompletion cfg m) "re-opens after editing"
          }
          test "the popup renders in the view" {
              let s = Surface(Size.Create(40, 16))
              let m = (fresh ()) |> typeText "/h"
              Layout.render s (Layout.measure (Rect.Create(0, 0, 40, 16)) (AgentShell.view cfg m))

              let all =
                  String.concat "\n" [ for y in 0..15 -> String.concat "" [ for x in 0..39 -> s.[x, y].Grapheme ] ]

              Expect.stringContains all "history" "candidates painted"
          } ]

[<Tests>]
let scrollTests =
    testList
        "AgentShell scrolling"
        [ test "scrolling up stops following; new output then doesn't yank the view" {
              let m = tall (fresh ()) |> AgentShell.followTail
              let m = m |> upd (ScrollBy -5)
              Expect.isFalse m.Following "scrolled away"
              let offset = m.Offset
              let m = AgentShell.addAssistant "new output" m
              Expect.isFalse m.Following "still not following"
              Expect.equal m.Offset offset "offset unchanged by new output"
          }
          test "PageDown back to the bottom resumes following; Esc jumps to latest" {
              let m = tall (fresh ()) |> AgentShell.followTail |> press PageUp
              Expect.isFalse m.Following "page up left the tail"
              let m = m |> press PageDown |> press PageDown |> press PageDown
              Expect.isTrue m.Following "reaching the bottom resumes following"
              let m = m |> upd (ScrollBy -3) |> press Escape
              Expect.isTrue m.Following "Esc jumped back to the latest output"
          }
          test "submitting jumps to the latest output" {
              let m = tall (fresh ()) |> upd (ScrollBy -10) |> typeText "hi" |> press Enter
              Expect.isTrue m.Following "submit → follow"
          }
          test "a resize keeps the same entry at the top when not following" {
              let m = tall (fresh ()) |> AgentShell.followTail |> upd (ScrollBy -12)

              let topEntry (m: ShellModel) =
                  // the first transcript row painted inside the transcript box
                  let s = Surface(m.Size)
                  Layout.render s (Layout.measure (Rect.FromOrigin m.Size) (AgentShell.view cfg m))
                  String.concat "" [ for x in 0 .. m.Size.Width - 1 -> s.[x, 4].Grapheme ]

              let before = topEntry m
              let m2 = upd (Resized(Size.Create(60, 16))) m
              Expect.stringContains (topEntry m2) (before.Trim('│', ' ', '█', '░').Trim()) "same content at the top"
          } ]

[<Tests>]
let sessionTests =
    testList
        "AgentShell session state"
        [ test "finishing one of two concurrent replies keeps the session Streaming" {
              let a, m = AgentShell.startReply (fresh ())
              let b, m = AgentShell.startReply m
              let m = AgentShell.finishReply a m
              Expect.equal m.Session Streaming "b still streams"
              let m = AgentShell.finishReply b m
              Expect.equal m.Session Idle "now idle"
          }
          test "finishing a reply while an approval is open keeps AwaitingApproval" {
              let id, m = AgentShell.startReply (fresh ())
              let m = AgentShell.requestApproval "x" m |> AgentShell.finishReply id
              Expect.equal m.Session AwaitingApproval "approval still pending"
          }
          test "a second approval queues instead of replacing the first" {
              let id1, m = AgentShell.requestApprovalId "first" (fresh ())
              let id2, m = AgentShell.requestApprovalId "second" m
              Expect.notEqual id1 id2 "distinct ids"
              Expect.equal (m.Approval |> Option.map (fun a -> a.Command)) (Some "first") "first still shown"
              let m = m |> press Enter
              Expect.equal (m.Approval |> Option.map (fun a -> a.Command)) (Some "second") "second opens next"
              let m = m |> press Enter
              Expect.equal (blocks m) [ AssistantMd "true first"; AssistantMd "true second" ] "both resolved in order"
              Expect.equal m.Session Idle "idle after the queue drains"
          }
          test "cancelApproval withdraws a queued approval by id" {
              let _, m = AgentShell.requestApprovalId "a" (fresh ())
              let id2, m = AgentShell.requestApprovalId "b" m
              let m = AgentShell.cancelApproval id2 m
              Expect.isEmpty m.ApprovalQueue "queued approval withdrawn"
          }
          test "Esc while streaming cancels: stream stops, late chunks are dropped, OnCancel runs" {
              let mutable cancelled = false

              let cfg2 =
                  { cfg with
                      OnCancel =
                          fun m ->
                              cancelled <- true
                              m, Cmd.none }

              let id, m = AgentShell.startReply (fresh ())
              let m = AgentShell.stream id "partial" m
              let m = AgentShell.update cfg2 (KeyPress(plain Escape)) m |> fst
              Expect.equal m.Session Idle "no longer streaming"
              Expect.isTrue cancelled "OnCancel ran"
              let m = AgentShell.stream id " late" m
              Expect.equal (blocks m |> List.head) (AssistantMd "partial") "late chunk dropped"
          }
          test "failReply finishes the stream and records the error" {
              let id, m = AgentShell.startReply (fresh ())
              let m = AgentShell.failReply id "boom" m
              Expect.equal m.Session Idle "idle"
              Expect.contains (blocks m) (ErrorBlock "boom") "error recorded"
          } ]

[<Tests>]
let multilineTests =
    testList
        "AgentShell multiline prompt"
        [ test "the prompt grows with its lines, up to MaxPromptRows" {
              let nl = KeyPress(key Enter { KeyModifiers.None with Shift = true })

              let m =
                  (fresh ()) |> typeText "a" |> upd nl |> typeText "b" |> upd nl |> typeText "c"

              Expect.equal (AgentShell.promptRows cfg.MaxPromptRows m) 3 "three rows"
              let many = [ 1..10 ] |> List.fold (fun m _ -> m |> upd nl |> typeText "x") m
              Expect.equal (AgentShell.promptRows cfg.MaxPromptRows many) cfg.MaxPromptRows "capped"
          }
          test "a multi-line paste is shown on several prompt rows" {
              let m = upd (Edit(Paste "first\nsecond")) (fresh ())
              let s = Surface(Size.Create(40, 16))
              Layout.render s (Layout.measure (Rect.Create(0, 0, 40, 16)) (AgentShell.view cfg m))

              let row y =
                  String.concat "" [ for x in 0..39 -> s.[x, y].Grapheme ]

              Expect.stringContains (row 13) "first" "first line"
              Expect.stringContains (row 14) "second" "second line on its own row"
          } ]

[<Tests>]
let streamingCostTests =
    testList
        "Streaming cost"
        [ test "streaming a reply doesn't re-measure the transcript per token" {
              let id, m = AgentShell.startReply (tall (fresh ()))
              let sw = System.Diagnostics.Stopwatch.StartNew()

              let m =
                  [ 1..3000 ]
                  |> List.fold (fun m i -> AgentShell.stream id (sprintf "tok%d " i) m) m

              // Per-token work is an append (no layout); render once at the end.
              Expect.isLessThan sw.Elapsed.TotalSeconds 5.0 "3000 tokens stream quickly"
              let s = Surface(m.Size)
              Layout.render s (Layout.measure (Rect.FromOrigin m.Size) (AgentShell.view cfg m))
          }
          test "the session cache keeps one revision per message while it streams" {
              let id, m = AgentShell.startReply (fresh ())
              let mutable m = m

              for i in 1..200 do
                  m <- AgentShell.stream id (sprintf "w%d " i) m
                  // a frame
                  let s = Surface(m.Size)
                  Layout.render s (Layout.measure (Rect.FromOrigin m.Size) (AgentShell.view cfg m))

              Expect.isLessThanOrEqual m.Cache.Count 1 "one entry for the one message (not 200 prefixes)"
          }
          test "the shared height cache is bounded by retained text, not just entry count" {
              let w = 60

              for i in 1..400 do
                  ChatTranscript.blockHeight theme w 0 (AssistantMd(String.replicate 20000 "x" + string i))
                  |> ignore

              Expect.isLessThanOrEqual
                  (ChatTranscript.heightCacheRetainedChars ())
                  (4 * 1024 * 1024)
                  "retained text stays under the byte budget"
          }
          test "one enormous tool-output block paints only its visible rows" {
              let out = String.concat "\n" [ for i in 1..20000 -> sprintf "output line %d" i ]
              let blocks = [ ToolCall("shell", "cat big.log", Succeeded, "1s", out) ]

              let node: LayoutNode<unit> =
                  ChatTranscript.view theme 60 0 10 5000 Style.Default Style.Default blocks

              let sw = System.Diagnostics.Stopwatch.StartNew()
              let s = Surface(Size.Create(62, 10))
              Layout.render s (Layout.measure (Rect.Create(0, 0, 62, 10)) node)
              Expect.isLessThan sw.Elapsed.TotalSeconds 5.0 "renders promptly"

              let painted = String.concat "" [ for x in 0..61 -> s.[x, 0].Grapheme ]

              Expect.stringContains painted "output line" "the scrolled-to rows are painted"
          } ]
