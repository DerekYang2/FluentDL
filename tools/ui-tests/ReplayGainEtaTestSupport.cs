using Microsoft.UI.Xaml;

namespace FluentDL.Views;

// Only compiled in the explicit conversion UI fixture. Exercises the actual progress text with a manual clock.
public sealed partial class ReplayGainDialog
{
    internal async Task ShowEtaFixtureAsync(XamlRoot root, bool writing)
    {
        Dialog.XamlRoot = root;
        SetState(writing ? State.Writing : State.Analyzing);
        ShowProgress(writing ? "Writing tags to" : "Analyzing", 0, 10);
        LocalConversionTestSupport.Clock.Advance(10);
        ShowProgress(writing ? "Writing tags to" : "Analyzing", 1, 10);
        Dialog.IsSecondaryButtonEnabled = false;
        try { await Dialog.ShowAsync(); }
        finally { SetState(State.Ready); }
    }
}
