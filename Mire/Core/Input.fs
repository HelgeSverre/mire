namespace Mire.Core

type Key =
    | Char of string
    | Enter
    | Escape
    | Backspace
    | Tab
    | ArrowUp
    | ArrowDown
    | ArrowLeft
    | ArrowRight
    | Home
    | End
    | PageUp
    | PageDown
    | Function of int
    | Space
    | Delete
    | Insert
    | Unknown of string

type KeyModifiers =
    { Shift: bool
      Ctrl: bool
      Alt: bool
      Meta: bool }

    static member None =
        { Shift = false
          Ctrl = false
          Alt = false
          Meta = false }

type KeyEventType =
    | Press
    | Repeat
    | Release

type KeyEvent =
    { Key: Key
      Text: string option
      Modifiers: KeyModifiers
      Repeat: bool
      EventType: KeyEventType }

type MouseButton =
    | Left
    | Middle
    | Right
    | ScrollUp
    | ScrollDown
    | ScrollLeft
    | ScrollRight
    | UnknownButton of int

type MouseEvent =
    {
        X: int
        Y: int
        Button: MouseButton
        Modifiers: KeyModifiers
        Pressed: bool
        /// True when this is a motion report (the SGR `0x20` motion bit) rather than a
        /// fresh press/release — i.e. the pointer moved. With button tracking (mode
        /// 1002) this is a drag of `Button`; it lets an app tell a drag from a click.
        Moved: bool
    }

/// The terminal's reported light/dark color scheme (DEC mode 2031 / DSR 996).
type ColorScheme =
    | Dark
    | Light

type InputEvent =
    | Key of KeyEvent
    | Mouse of MouseEvent
    | Paste of string
    | Resize of Size
    | FocusGained
    | FocusLost
    /// The terminal's color scheme changed (or was first reported). Requires
    /// theme notifications to be enabled — `Program.withThemeNotifications`.
    | ThemeChanged of ColorScheme
    | Tick of System.TimeSpan
