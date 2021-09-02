using Stylet;

namespace MultiFunPlayer.Common.Controls.ViewModels
{
    public class ErrorMessageDialogViewModel : Screen
    {
        public string Message { get; }

        public ErrorMessageDialogViewModel(string message) => Message = message;

        public override bool Equals(object obj)
            => obj != null && GetType() == obj.GetType() && GetHashCode() == obj.GetHashCode();

        public override int GetHashCode() => Message.GetHashCode();
    }
}
