using CommunityToolkit.Mvvm.ComponentModel;

namespace EnderDrive.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Se llama cada vez que la página pasa a mostrarse en el menú lateral.
    /// Las páginas que necesiten datos frescos (por ejemplo, la lista de copias) lo sobrescriben.
    /// </summary>
    public virtual void OnNavigatedTo()
    {
    }
}
