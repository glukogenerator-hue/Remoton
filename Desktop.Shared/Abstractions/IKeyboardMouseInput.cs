using Remotely.Desktop.Shared.Enums;
using Remotely.Desktop.Shared.Services;

namespace Remotely.Desktop.Shared.Abstractions;

public interface IKeyboardMouseInput
{
    void Init();
    /// <param name="key">KeyboardEvent.key: символ или имя клавиши.</param>
    /// <param name="code">KeyboardEvent.code: физическая клавиша, если клиент её передал.</param>
    void SendKeyDown(string key, string? code = null);
    void SendKeyUp(string key, string? code = null);
    void SendMouseMove(double percentX, double percentY, IViewer viewer);
    void SendMouseWheel(int deltaY);
    void SendText(string transferText);
    void ToggleBlockInput(bool toggleOn);
    void SetKeyStatesUp();
    void SendMouseButtonAction(int button, ButtonAction buttonAction, double percentX, double percentY, IViewer viewer);
}
