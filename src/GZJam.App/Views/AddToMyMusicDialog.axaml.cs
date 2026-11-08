using Avalonia.Controls;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>Add to my music: which folder of My music the songs go to (#324). Returns the place, or null if canceled.</summary>
public partial class AddToMyMusicDialog : Window
{
    public AddToMyMusicDialog() : this(new AddPlaceViewModel("", "", "", [], null, false, false))
    {
    }

    public AddToMyMusicDialog(AddPlaceViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        AddButton.Click += (_, _) => Close(model.Result());
        CancelButton.Click += (_, _) => Close(null);
        UsualButton.Click += (_, _) => model.UseUsualPlace();
        NewFolderButton.Click += async (_, _) =>
        {
            if (await new PromptDialog("New folder", $"Inside {model.IntoText}", "Make it").ShowDialog<string?>(this) is { } name)
                model.MakeFolder(name);
        };
    }
}
