namespace TuroClawProwl.Application.Ports;

public interface IAutostartManager
{
    bool IsEnabled();

    void Enable(string executablePath);

    void Disable();
}
