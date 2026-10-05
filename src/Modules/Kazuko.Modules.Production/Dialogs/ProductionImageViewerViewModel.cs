using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Production.Dialogs;

public sealed class ProductionImageViewerViewModel : ObservableObject
{
    private double _zoom = 1.0;

    public ProductionImageViewerViewModel(string imagePath, string title)
    {
        ImagePath = imagePath;
        Title = title;
    }

    public string ImagePath { get; }
    public string Title { get; }

    public double Zoom
    {
        get => _zoom;
        private set => SetProperty(ref _zoom, Math.Clamp(value, 0.25, 4.0));
    }

    public void ZoomIn() => Zoom = Math.Round(Zoom * 1.15, 2);

    public void ZoomOut() => Zoom = Math.Round(Zoom / 1.15, 2);
}
