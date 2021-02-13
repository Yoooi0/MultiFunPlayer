using MultiFunPlayer.OutputTarget;
using Stylet;
using System;
using System.Collections.Generic;

namespace MultiFunPlayer.ViewModels
{
    public class OutputTargetViewModel : Conductor<IOutputTarget>.Collection.AllActive, IDisposable
    {
        public OutputTargetViewModel(IEnumerable<IOutputTarget> targets)
        {
            foreach (var target in targets)
                Items.Add(target);
        }

        protected override void OnActivate()
        {
            ActivateAndSetParent(Items);
            base.OnActivate();
        }

        protected virtual void Dispose(bool disposing) { }

        void IDisposable.Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
