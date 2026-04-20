using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

namespace Lyrical.Services;

public static class NewSongDialog
{
    public static async Task<string?> PromptAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null)
        {
            return null;
        }

        var input = new TextBox
        {
            PlaceholderText = "Song title",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "New song",
            Content = input,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (d, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                d.IsPrimaryButtonEnabled = false;
            }
        };

        input.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        };

        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
        {
            return null;
        }

        return input.Text.Trim();
    }
}
