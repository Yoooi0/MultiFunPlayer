using MultiFunPlayer.Script.Repository;

namespace MultiFunPlayer.UI.Dialogs.ViewModels;

internal sealed class ScriptRepositoryManagerDialog(IScriptRepositoryManager scriptRepositoryManager)
{
    public IReadOnlyCollection<IScriptRepository> Repositories => scriptRepositoryManager.Repositories;
}