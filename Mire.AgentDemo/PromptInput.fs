namespace Mire.AgentDemo

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// A deliberately-minimal single-line input. The framework has no text-buffer/`Input`
/// widget yet (ROADMAP v0.3), so this is a PLACEHOLDER behind a thin seam: append at
/// the end, delete the last char, submit on Enter. When a real widget lands, only this
/// module and the prompt's `mapInput` cases change.
type PromptInput = { Value: string }

module PromptInput =

    let empty = { Value = "" }

    let append (s: string) (p: PromptInput) = { p with Value = p.Value + s }

    let backspace (p: PromptInput) =
        if p.Value.Length = 0 then
            p
        else
            { p with
                Value = p.Value.Substring(0, p.Value.Length - 1) }

    /// Render the prompt line: an accent glyph, then the value with a block cursor,
    /// or the placeholder when empty.
    let render
        (glyphStyle: Style)
        (textStyle: Style)
        (placeholderStyle: Style)
        (placeholder: string)
        (p: PromptInput)
        : LayoutNode<'msg> =
        let body =
            if p.Value = "" then
                Text.text placeholder placeholderStyle
            else
                Text.text (p.Value + "▏") textStyle

        Stack.hstackOf
            [ Stack.sized (Length.Cells 2) (Text.text "❯ " glyphStyle)
              Stack.sized Length.Fill body ]
