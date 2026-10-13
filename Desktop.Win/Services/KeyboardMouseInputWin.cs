using Remotely.Desktop.Native.Windows;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Enums;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.UI.Services;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Remotely.Desktop.Native.Windows.User32;

namespace Remotely.Desktop.Win.Services;

[SupportedOSPlatform("windows")]
public class KeyboardMouseInputWin(
    IUiDispatcher _dispatcher,
    ILogger<KeyboardMouseInputWin> _logger) : IKeyboardMouseInput
{
    private readonly ConcurrentQueue<Action> _inputActions = new();
    private readonly AutoResetEvent _inputReadySignal = new(false);
    private volatile bool _inputBlocked;
    private Thread? _inputProcessingThread;

    /// <summary>
    /// Нажатые сейчас клавиши и способ, которым они были отправлены (по code, а если его нет, по key).
    /// Отпускание повторяет способ нажатия: иначе клавиша «залипнет», если Ctrl отпустили раньше неё.
    /// </summary>
    private readonly ConcurrentDictionary<string, SentKey> _pressedKeys = new();

    private readonly record struct SentKey(VirtualKey? Vk, ushort? Unicode);

    [Flags]
    private enum ShiftState : byte
    {
        None = 0,
        ShiftPressed = 1 << 0,
        CtrlPressed = 1 << 1,
        AltPressed = 1 << 2,
        HankakuPressed = 1 << 3,
        Reserved1 = 1 << 4,
        Reserved2 = 1 << 5,
    }

    public void Init()
    {
        StartInputProcessingThread();
    }

    public void SendKeyDown(string key, string? code = null)
    {
        TryOnInputDesktop(() =>
        {
            try
            {
                try
                {
                    if (!TryCreateKeyInput(key, code, true, out var input))
                    {
                        _logger.LogWarning("Unable to simulate key input {key}.", key);
                        return;
                    }

                    var sent = SendInput(1, [input], INPUT.Size);
                    if (sent == 0)
                    {
                        _logger.LogWarning(
                            "Failed to send input for key {Key}.  Last Win32 Error: {Win32Error}",
                            key,
                            Marshal.GetLastPInvokeError());
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while sending key up.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending key down.");
            }
        });
    }

    public void SendKeyUp(string key, string? code = null)
    {
        TryOnInputDesktop(() =>
        {
            try
            {
                if (!TryCreateKeyInput(key, code, false, out var input))
                {
                    _logger.LogWarning("Unable to simulate key input {key}.", key);
                    return;
                }

                var sent = SendInput(1, [input], INPUT.Size);
                if (sent == 0)
                {
                    _logger.LogWarning(
                        "Failed to send input for key {Key}.  Last Win32 Error: {Win32Error}",
                        key,
                        Marshal.GetLastPInvokeError());
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending key up.");
            }
        });
    }

    public void SendMouseButtonAction(int button, ButtonAction buttonAction, double percentX, double percentY, IViewer viewer)
    {
        TryOnInputDesktop(() =>
        {
            try
            {
                MOUSEEVENTF mouseEvent;
                switch (button)
                {
                    case 0:
                        switch (buttonAction)
                        {
                            case ButtonAction.Down:
                                mouseEvent = MOUSEEVENTF.LEFTDOWN;
                                break;
                            case ButtonAction.Up:
                                mouseEvent = MOUSEEVENTF.LEFTUP;
                                break;
                            default:
                                return;
                        }
                        break;
                    case 1:
                        switch (buttonAction)
                        {
                            case ButtonAction.Down:
                                mouseEvent = MOUSEEVENTF.MIDDLEDOWN;
                                break;
                            case ButtonAction.Up:
                                mouseEvent = MOUSEEVENTF.MIDDLEUP;
                                break;
                            default:
                                return;
                        }
                        break;
                    case 2:
                        switch (buttonAction)
                        {
                            case ButtonAction.Down:
                                mouseEvent = MOUSEEVENTF.RIGHTDOWN;
                                break;
                            case ButtonAction.Up:
                                mouseEvent = MOUSEEVENTF.RIGHTUP;
                                break;
                            default:
                                return;
                        }
                        break;
                    default:
                        return;
                }
                var xyPercent = GetAbsolutePercentFromRelativePercent(percentX, percentY, viewer.Capturer);
                // Coordinates must be normalized.  The bottom-right coordinate is mapped to 65535.
                var normalizedX = xyPercent.Item1 * 65535D;
                var normalizedY = xyPercent.Item2 * 65535D;
                var union = new InputUnion()
                {
                    mi = new MOUSEINPUT()
                    {
                        dwFlags = MOUSEEVENTF.ABSOLUTE | mouseEvent | MOUSEEVENTF.VIRTUALDESK,
                        dx = (int)normalizedX,
                        dy = (int)normalizedY,
                        time = 0,
                        mouseData = 0,
                        dwExtraInfo = GetMessageExtraInfo()
                    }
                };
                var input = new INPUT() { type = InputType.MOUSE, U = union };
                var sent = SendInput(1, [input], INPUT.Size);
                if (sent == 0)
                {
                    _logger.LogWarning(
                        "Failed to send input for button {Button}.  Last Win32 Error: {Win32Error}",
                        button,
                        Marshal.GetLastPInvokeError());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending mouse button.");
            }
        });
    }

    public void SendMouseMove(double percentX, double percentY, IViewer viewer)
    {
        TryOnInputDesktop(() =>
        {
            try
            {
                if (!Win32Interop.SwitchToInputDesktop())
                {
                    _logger.LogWarning("Desktop switch failed during mouse move.");
                }
                var xyPercent = GetAbsolutePercentFromRelativePercent(percentX, percentY, viewer.Capturer);
                // Coordinates must be normalized.  The bottom-right coordinate is mapped to 65535.
                var normalizedX = xyPercent.Item1 * 65535D;
                var normalizedY = xyPercent.Item2 * 65535D;
                var union = new InputUnion() { mi = new MOUSEINPUT() { dwFlags = MOUSEEVENTF.ABSOLUTE | MOUSEEVENTF.MOVE | MOUSEEVENTF.VIRTUALDESK, dx = (int)normalizedX, dy = (int)normalizedY, time = 0, mouseData = 0, dwExtraInfo = GetMessageExtraInfo() } };
                var input = new INPUT() { type = InputType.MOUSE, U = union };
                var sent = SendInput(1, [input], INPUT.Size);
                if (sent == 0)
                {
                    _logger.LogWarning(
                        "Failed to send mouse move.  Last Win32 Error: {Win32Error}",
                        Marshal.GetLastPInvokeError());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending mouse move.");
            }
        });
    }

    public void SendMouseWheel(int deltaY)
    {
        TryOnInputDesktop(() =>
        {
            try
            {
                if (deltaY < 0)
                {
                    deltaY = -120;
                }
                else if (deltaY > 0)
                {
                    deltaY = 120;
                }
                var union = new InputUnion() { mi = new MOUSEINPUT() { dwFlags = MOUSEEVENTF.WHEEL, dx = 0, dy = 0, time = 0, mouseData = deltaY, dwExtraInfo = GetMessageExtraInfo() } };
                var input = new INPUT() { type = InputType.MOUSE, U = union };
                var sent = SendInput(1, [input], INPUT.Size);
                if (sent == 0)
                {
                    _logger.LogWarning(
                        "Failed to send mouse wheel.  Last Win32 Error: {Win32Error}",
                        Marshal.GetLastPInvokeError());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending mouse wheel.");
            }
        });
    }

    public void SendText(string transferText)
    {
        TryOnInputDesktop(() =>
        {
            try
            {
                foreach (var character in transferText)
                {
                    var keyCode = Convert.ToUInt16(character);

                    var keyDown = CreateKeyboardInput(keyCode, true);
                    var result = SendInput(1, [keyDown], INPUT.Size);
                    if (result != 1)
                    {
                        _logger.LogWarning(
                            "Send text failed. Failed to simulate input for character {Character}.",
                            character);
                        break;
                    }

                    var keyUp = CreateKeyboardInput(keyCode, false);
                    result = SendInput(1, [keyUp], INPUT.Size);
                    if (result != 1)
                    {
                        _logger.LogWarning(
                            "Send text failed. Failed to simulate input for character {Character}.",
                            character);
                        break;
                    }

                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending text.");
            }
        });
    }

    public void SetKeyStatesUp()
    {
        TryOnInputDesktop(() =>
        {
            // Сначала отпускаем клавиши, которые нажимали мы сами (в том числе Unicode-символы).
            foreach (var sent in _pressedKeys.Values)
            {
                SendInput(1, [CreateKeyInput(sent, false)], INPUT.Size);
            }
            _pressedKeys.Clear();

            foreach (VirtualKey key in Enum.GetValues(typeof(VirtualKey)))
            {
                try
                {
                    // Skip mouse buttons and toggleable keys.
                    switch (key)
                    {
                        case VirtualKey.LBUTTON:
                        case VirtualKey.RBUTTON:
                        case VirtualKey.MBUTTON:
                        case VirtualKey.NUMLOCK:
                        case VirtualKey.CAPITAL:
                        case VirtualKey.SCROLL:
                            continue;
                        default:
                            break;
                    }
                    var (isPressed, isToggled) = GetKeyPressState(key);
                    if (isPressed || isToggled)
                    {
                        var input = CreateKeyboardInput(key, false);
                        var sent = SendInput(1, [input], INPUT.Size);
                        if (sent == 0)
                        {
                            _logger.LogWarning(
                                "Failed to set key up for key {Key}.  Last Win32 Error: {Win32Error}",
                                key,
                                Marshal.GetLastPInvokeError());
                        }
                        Thread.Sleep(1);
                    }
                }
                catch { }
            }
        });
    }

    public void ToggleBlockInput(bool toggleOn)
    {
        TryOnInputDesktop(() =>
        {
            _inputBlocked = toggleOn;
            var result = BlockInput(toggleOn);
            _logger.LogInformation("Result of ToggleBlockInput set to {toggleOn}: {result}", toggleOn, result);
        });
    }

    private static INPUT CreateKeyboardInput(
        VirtualKey virtualKey,
        bool isPressed)
    {
        KEYEVENTF flags = 0;

        if (IsExtendedKey(virtualKey))
        {
            flags |= KEYEVENTF.EXTENDEDKEY;
        }

        if (!isPressed)
        {
            flags |= KEYEVENTF.KEYUP;
        }

        return new INPUT()
        {
            type = InputType.KEYBOARD,
            U = new InputUnion()
            {
                ki = new KEYBDINPUT()
                {
                    wVk = virtualKey,
                    wScan = (ushort)MapVirtualKeyEx((uint)virtualKey, VkMapType.MAPVK_VK_TO_VSC_EX, GetKeyboardLayout(0)),
                    dwExtraInfo = GetMessageExtraInfo(),
                    dwFlags = flags,
                    time = 0
                }
            }
        };
    }

    private static INPUT CreateKeyboardInput(ushort unicodeKey, bool isPressed)
    {
        var flags = KEYEVENTF.UNICODE;
        if (!isPressed)
        {
            flags |= KEYEVENTF.KEYUP;
        }

        return new INPUT()
        {
            type = InputType.KEYBOARD,
            U = new InputUnion()
            {
                ki = new KEYBDINPUT()
                {
                    wVk = 0,
                    wScan = unicodeKey,
                    dwFlags = flags,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        };
    }

    private static Tuple<double, double> GetAbsolutePercentFromRelativePercent(double percentX, double percentY, IScreenCapturer capturer)
    {
        var absoluteX = capturer.CurrentScreenBounds.Width * percentX + capturer.CurrentScreenBounds.Left - capturer.GetVirtualScreenBounds().Left;
        var absoluteY = capturer.CurrentScreenBounds.Height * percentY + capturer.CurrentScreenBounds.Top - capturer.GetVirtualScreenBounds().Top;
        return new Tuple<double, double>(absoluteX / capturer.GetVirtualScreenBounds().Width, absoluteY / capturer.GetVirtualScreenBounds().Height);
    }

    private static (bool Pressed, bool Toggled) GetKeyPressState(VirtualKey vkey)
    {
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate#return-value
        var state = GetKeyState(vkey);
        var pressed = state < 0;
        var toggled = (state & 1) != 0;
        return (pressed, toggled);
    }

    private static bool IsExtendedKey(VirtualKey virtualKey)
    {
        return virtualKey switch
        {
            VirtualKey.SHIFT or
            VirtualKey.CONTROL or
            VirtualKey.MENU or
            VirtualKey.RCONTROL or
            VirtualKey.RMENU or
            VirtualKey.INSERT or
            VirtualKey.DELETE or
            VirtualKey.HOME or
            VirtualKey.END or
            VirtualKey.PRIOR or
            VirtualKey.NEXT or
            VirtualKey.LEFT or
            VirtualKey.RIGHT or
            VirtualKey.UP or
            VirtualKey.DOWN or
            VirtualKey.NUMLOCK or
            VirtualKey.CANCEL or
            VirtualKey.DIVIDE or
            VirtualKey.SNAPSHOT or
            VirtualKey.RETURN => true,
            _ => false
        };
    }

    private bool ConvertJavaScriptKeyToVirtualKey(string key, [NotNullWhen(true)] out VirtualKey? result)
    {
        result = key switch
        {
            " " => VirtualKey.SPACE,
            "Down" or "ArrowDown" => VirtualKey.DOWN,
            "Up" or "ArrowUp" => VirtualKey.UP,
            "Left" or "ArrowLeft" => VirtualKey.LEFT,
            "Right" or "ArrowRight" => VirtualKey.RIGHT,
            "Enter" => VirtualKey.RETURN,
            "Esc" or "Escape" => VirtualKey.ESCAPE,
            "Alt" => VirtualKey.MENU,
            "Control" => VirtualKey.CONTROL,
            "Shift" => VirtualKey.SHIFT,
            "PAUSE" => VirtualKey.PAUSE,
            "BREAK" => VirtualKey.PAUSE,
            "Backspace" => VirtualKey.BACK,
            "Tab" => VirtualKey.TAB,
            "CapsLock" => VirtualKey.CAPITAL,
            "Delete" => VirtualKey.DELETE,
            "Home" => VirtualKey.HOME,
            "End" => VirtualKey.END,
            "PageUp" => VirtualKey.PRIOR,
            "PageDown" => VirtualKey.NEXT,
            "NumLock" => VirtualKey.NUMLOCK,
            "Insert" => VirtualKey.INSERT,
            "ScrollLock" => VirtualKey.SCROLL,
            "F1" => VirtualKey.F1,
            "F2" => VirtualKey.F2,
            "F3" => VirtualKey.F3,
            "F4" => VirtualKey.F4,
            "F5" => VirtualKey.F5,
            "F6" => VirtualKey.F6,
            "F7" => VirtualKey.F7,
            "F8" => VirtualKey.F8,
            "F9" => VirtualKey.F9,
            "F10" => VirtualKey.F10,
            "F11" => VirtualKey.F11,
            "F12" => VirtualKey.F12,
            "Meta" => VirtualKey.LWIN,
            "ContextMenu" => VirtualKey.APPS,
            "PrintScreen" => VirtualKey.SNAPSHOT,
            "Clear" => VirtualKey.CLEAR,
            _ => null
        };

        return result is not null;
    }

    private static INPUT CreateKeyInput(SentKey sent, bool isPressed)
    {
        return sent.Vk is { } vk ?
            CreateKeyboardInput(vk, isPressed) :
            CreateKeyboardInput(sent.Unicode!.Value, isPressed);
    }

    /// <summary>
    /// Печатаемые Ctrl/Alt/Win-сочетания. Ctrl+Alt без Win — это AltGr на многих раскладках,
    /// поэтому такой символ вводится как есть.
    /// </summary>
    private bool IsShortcutActive()
    {
        bool ctrl = false, alt = false, win = false;
        foreach (var sent in _pressedKeys.Values)
        {
            switch (sent.Vk)
            {
                case VirtualKey.CONTROL or VirtualKey.LCONTROL or VirtualKey.RCONTROL:
                    ctrl = true;
                    break;
                case VirtualKey.MENU or VirtualKey.LMENU or VirtualKey.RMENU:
                    alt = true;
                    break;
                case VirtualKey.LWIN or VirtualKey.RWIN:
                    win = true;
                    break;
            }
        }
        return win || (ctrl != alt);
    }

    /// <summary>
    /// Физическая клавиша по KeyboardEvent.code. Не зависит от раскладки клиента и хоста.
    /// </summary>
    private static bool TryConvertCodeToVirtualKey(string? code, out VirtualKey result)
    {
        result = default;
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        if (code.Length == 4 && code.StartsWith("Key") && code[3] is >= 'A' and <= 'Z')
        {
            result = (VirtualKey)code[3];
            return true;
        }

        if (code.Length == 6 && code.StartsWith("Digit") && code[5] is >= '0' and <= '9')
        {
            result = (VirtualKey)code[5];
            return true;
        }

        if (code.Length == 7 && code.StartsWith("Numpad") && code[6] is >= '0' and <= '9')
        {
            result = (VirtualKey)((short)VirtualKey.NUMPAD0 + (code[6] - '0'));
            return true;
        }

        VirtualKey? mapped = code switch
        {
            "Minus" => VirtualKey.OEM_MINUS,
            "Equal" => VirtualKey.OEM_PLUS,
            "BracketLeft" => VirtualKey.OEM_4,
            "BracketRight" => VirtualKey.OEM_6,
            "Backslash" => VirtualKey.OEM_5,
            "Semicolon" => VirtualKey.OEM_1,
            "Quote" => VirtualKey.OEM_7,
            "Comma" => VirtualKey.OEM_COMMA,
            "Period" => VirtualKey.OEM_PERIOD,
            "Slash" => VirtualKey.OEM_2,
            "Backquote" => VirtualKey.OEM_3,
            "IntlBackslash" => VirtualKey.OEM_102,
            "NumpadMultiply" => VirtualKey.MULTIPLY,
            "NumpadAdd" => VirtualKey.ADD,
            "NumpadSubtract" => VirtualKey.SUBTRACT,
            "NumpadDecimal" => VirtualKey.DECIMAL,
            "NumpadDivide" => VirtualKey.DIVIDE,
            "Space" => VirtualKey.SPACE,
            _ => null
        };

        if (mapped is null)
        {
            return false;
        }

        result = mapped.Value;
        return true;
    }

    /// <summary>
    /// Выбирает способ ввода. Именованные клавиши идут как виртуальные.
    /// Символ вводится как Unicode (работает в любой раскладке, включая русскую), а в сочетаниях
    /// с Ctrl/Alt/Win используется физическая клавиша, иначе приложения не распознают горячие клавиши.
    /// </summary>
    private bool TryResolveKey(string key, string? code, out SentKey resolved)
    {
        resolved = default;

        if (ConvertJavaScriptKeyToVirtualKey(key, out var named))
        {
            resolved = new SentKey(named, null);
            return true;
        }

        if (key.Length != 1)
        {
            return false;
        }

        var character = key[0];

        if (IsShortcutActive())
        {
            if (TryConvertCodeToVirtualKey(code, out var physical))
            {
                resolved = new SentKey(physical, null);
                return true;
            }

            // Старый клиент без code: пробуем найти клавишу в раскладке хоста.
            var scan = VkKeyScanEx(character, GetKeyboardLayout(0));
            if (scan != -1)
            {
                resolved = new SentKey((VirtualKey)(scan & 0xFF), null);
                return true;
            }
        }

        resolved = new SentKey(null, character);
        return true;
    }

    private bool TryCreateKeyInput(string key, string? code, bool isPressed, out INPUT input)
    {
        input = default;
        var id = string.IsNullOrEmpty(code) ? key : code;

        if (!isPressed && _pressedKeys.TryRemove(id, out var pressed))
        {
            input = CreateKeyInput(pressed, false);
            return true;
        }

        if (!TryResolveKey(key, code, out var resolved))
        {
            _logger.LogWarning("Unable to parse key input: {key}.", key);
            return false;
        }

        if (isPressed)
        {
            _pressedKeys[id] = resolved;
        }

        input = CreateKeyInput(resolved, isPressed);
        return true;
    }

    private void ProcessQueue(CancellationToken cancelToken)
    {
        while (!cancelToken.IsCancellationRequested)
        {
            try
            {
                _inputReadySignal.WaitOne();
                while (_inputActions.TryDequeue(out var action))
                {
                    action();
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during input queue processing.");
            }
        }

        _logger.LogInformation("Stopping input processing on thread.");
    }
    private void StartInputProcessingThread()
    {
        // After BlockInput is enabled, only simulated input coming from the same thread
        // will work.  So we have to start a new thread that runs continuously and
        // processes a queue of input events.
        _inputProcessingThread = new Thread(() =>
        {
            _logger.LogInformation("New input processing thread started on thread {threadId}.", Environment.CurrentManagedThreadId);

            if (_inputBlocked && !BlockInput(true))
            {
                _logger.LogWarning("Failed to block input on input processing start.");
            }
            ProcessQueue(_dispatcher.ApplicationExitingToken);
        });

        _inputProcessingThread.SetApartmentState(ApartmentState.MTA);
        _inputProcessingThread.Start();
    }

    private void TryOnInputDesktop(Action inputAction)
    {
        _inputActions.Enqueue(() =>
        {
            try
            {
                var switchResult = Win32Interop.SwitchToInputDesktop();

                // Try to perform the dequeued action whether or not the switch was successful.
                inputAction();

                if (!switchResult)
                {
                    _logger.LogWarning("Desktop switch failed during input processing.");

                    // Thread likely has hooks in current desktop.  SendKeys will create one with no way to unhook it.
                    // Start a new thread for processing input.
                    StartInputProcessingThread();
                    return;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during input queue processing.");
            }
        });
        _inputReadySignal.Set();
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct ShortHelper(short value)
    {
        [FieldOffset(0)]
        public short Value = value;
        [FieldOffset(0)]
        public byte Low;
        [FieldOffset(1)]
        public byte High;
    }
}
