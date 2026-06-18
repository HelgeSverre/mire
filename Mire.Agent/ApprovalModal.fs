namespace Mire.Agent

open Mire.Core
open Mire.Layout
open Mire.Widgets

/// A command/risk approval prompt — a centered modal with a title, an intro line,
/// the command about to run, an optional risk note, and Accept/Deny buttons.
/// Style-driven by an `AppTheme`; the app owns which button is focused and what
/// accept/deny actually do.
module ApprovalModal =

    /// Region ids tagging the Accept / Deny buttons, so an app can route clicks via
    /// the runtime's retained region table (`Program.withMouseRegion` + `Layout.regionAt`)
    /// instead of mirroring the geometry with `buttonHit`.
    let acceptRegion = RegionId "approval.accept"
    let denyRegion = RegionId "approval.deny"

    /// Render the approval modal (52 cells wide, auto-height). `acceptFocused`
    /// highlights the Accept button (else Deny). Clicks: tag-route via
    /// `acceptRegion`/`denyRegion` (preferred) or hand-test with `buttonHit`.
    let view
        (theme: AppTheme)
        (title: string)
        (intro: string)
        (command: string)
        (risk: string option)
        (acceptLabel: string)
        (denyLabel: string)
        (acceptFocused: bool)
        : LayoutNode<'msg> =
        let acceptStyle = if acceptFocused then theme.selection else theme.fgMuted
        let denyStyle = if acceptFocused then theme.fgMuted else theme.selection

        let buttons =
            Stack.hstackOf
                [ Stack.sized
                      Length.Content
                      (Focusable.region acceptRegion (Text.text (sprintf " [ %s ] " acceptLabel) acceptStyle))
                  Stack.sized (Length.Cells 3) (Text.text "   " theme.fg)
                  Stack.sized
                      Length.Content
                      (Focusable.region denyRegion (Text.text (sprintf " ‹ %s › " denyLabel) denyStyle)) ]

        let cmdLines =
            if command = "" then
                []
            else
                [ Text.text (sprintf " ❯ %s" command) theme.fg ]

        let riskLines =
            match risk with
            | Some r -> [ Text.text (sprintf " risk: %s" r) theme.warning ]
            | None -> []

        let bodyRows =
            [ Text.text "" theme.fg; Text.text (" " + intro) theme.fgMuted ]
            @ cmdLines
            @ riskLines
            @ [ Text.text "" theme.fg
                buttons
                Text.text " ←/→ or Tab move · Enter confirm · Esc deny" theme.fgSubtle ]

        // height = title (1) + body rows + box border (2)
        Modal.modal Style.Default theme.border theme.warning 52 (List.length bodyRows + 3) title (Stack.vstack bodyRows)

    /// Hit-test a click against the Accept/Deny buttons. Mirrors `view`'s geometry
    /// (width 52, top border + title row, then `[blank; intro] @ cmd @ risk @
    /// [blank; buttons; hint]`). Returns `Some true` for Accept, `Some false` for
    /// Deny, `None` for a miss. (No retained region hit-testing in the framework
    /// yet — this mirrors the layout by hand.)
    let buttonHit
        (command: string)
        (risk: string option)
        (acceptLabel: string)
        (denyLabel: string)
        (areaW: int)
        (areaH: int)
        (x: int)
        (y: int)
        : bool option =
        let cmdLen = if command = "" then 0 else 1

        let riskLen =
            match risk with
            | Some _ -> 1
            | None -> 0

        let h = (5 + cmdLen + riskLen) + 3
        let left = max 0 ((areaW - 52) / 2)
        let top = max 0 ((areaH - h) / 2)
        let rowY = top + 2 + (3 + cmdLen + riskLen)

        if y <> rowY then
            None
        else
            let aw = Grapheme.stringWidth (sprintf " [ %s ] " acceptLabel)
            let dw = Grapheme.stringWidth (sprintf " ‹ %s › " denyLabel)
            let ax0 = left + 1
            let dx0 = ax0 + aw + 3

            if x >= ax0 && x < ax0 + aw then Some true
            elif x >= dx0 && x < dx0 + dw then Some false
            else None
