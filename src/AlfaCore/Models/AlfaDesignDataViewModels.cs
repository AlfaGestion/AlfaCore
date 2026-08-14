namespace AlfaCore.Models;

public sealed record AlfaDataViewColumnSizing(
    string Key,
    int MinWidth,
    int DefaultWidth,
    int MaxWidth,
    bool Resizable = true)
{
    public int Clamp(int width)
        => Math.Clamp(width <= 0 ? DefaultWidth : width, MinWidth, MaxWidth);
}
