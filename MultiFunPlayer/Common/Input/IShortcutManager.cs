using NLog;
using Stylet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MultiFunPlayer.Common.Input
{
    public class GestureEventArgs : EventArgs
    {
        public GestureEventArgs(IInputGesture gesture) => Gesture = gesture;

        public bool Handled { get; set; }
        public IInputGesture Gesture { get; }
    }

    public interface IShortcutManager : IDisposable
    {
        bool HandleGestures { get; set; }

        event EventHandler<GestureEventArgs> OnGesture;

        IReadOnlyCollection<IShortcutActionDescriptor> Actions { get; }
        IReadOnlyDictionary<IInputGestureDescriptor, BindableCollection<IShortcutAction>> Bindings { get; }

        void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor);
        void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<IShortcutSetting> settings);
        void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutAction action);

        void RegisterAction(string name, Action<IInputGesture> action)
            => RegisterAction(name, new List<string>(), action.Method, action.Target);

        void RegisterAction<T0>(string name, string description0, Action<IInputGesture, T0> action)
            => RegisterAction(name, new List<string>() { description0 }, action.Method, action.Target);

        void RegisterAction<T0, T1>(string name, string description0, string description1, Action<IInputGesture, T0, T1> action)
            => RegisterAction(name, new List<string>() { description0, description1 }, action.Method, action.Target);

        void RegisterAction<T0, T1, T2>(string name, string description0, string description1, string description2, Action<IInputGesture, T0, T1, T2> action)
            => RegisterAction(name, new List<string>() { description0, description1, description2 }, action.Method, action.Target);

        void RegisterAction<T0, T1, T2, T3>(string name, string description0, string description1, string description2, string description3, Action<IInputGesture, T0, T1, T2, T3> action)
            => RegisterAction(name, new List<string>() { description0, description1, description2, description3 }, action.Method, action.Target);

        void RegisterAction(string name, List<string> descriptions, MethodInfo method, object target);

        void RegisterGesture(IInputGestureDescriptor gestureDescriptor);
        void UnregisterGesture(IInputGestureDescriptor gestureDescriptor);
    }

    public class ShortcutManager : IShortcutManager
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<IShortcutActionDescriptor, (Delegate Delegate, List<string> Descriptions)> _actions;
        private readonly ObservableConcurrentDictionary<IInputGestureDescriptor, BindableCollection<IShortcutAction>> _bindings;
        private readonly List<IInputProcessor> _processors;

        public event EventHandler<GestureEventArgs> OnGesture;

        public IReadOnlyCollection<IShortcutActionDescriptor> Actions => _actions.Keys;
        public IReadOnlyDictionary<IInputGestureDescriptor, BindableCollection<IShortcutAction>> Bindings => _bindings;

        public bool HandleGestures { get; set; } = true;

        public ShortcutManager(IEnumerable<IInputProcessor> processors)
        {
            _actions = new Dictionary<IShortcutActionDescriptor, (Delegate Delegate, List<string> Descriptions)>();
            _bindings = new ObservableConcurrentDictionary<IInputGestureDescriptor, BindableCollection<IShortcutAction>>();

            _processors = processors.ToList();
            foreach (var processor in _processors)
                processor.OnGesture += HandleGesture;
        }

        public void RegisterAction(string name, List<string> descriptions, MethodInfo method, object target)
        {
            var methodParameters = method.GetParameters();
            var parametersTypes = methodParameters.Select(p => p.ParameterType).ToArray();
            if (methodParameters.Length == 0 || methodParameters[0].ParameterType != typeof(IInputGesture))
                throw new ArgumentException($"Provided method must have \"{nameof(IInputGesture)}\" as its first argument", nameof(method));

            var delegateType = Type.GetType($"System.Action`{methodParameters.Length}").MakeGenericType(parametersTypes);
            var delegateInstance = method.CreateDelegate(delegateType, target);
            var descriptor = new ShortcutActionDescriptor(name);

            _actions.Add(descriptor, (delegateInstance, descriptions));
        }

        public void BindAction(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor)
        {
            if (gestureDescriptor == null || actionDescriptor == null)
                return;

            RegisterGesture(gestureDescriptor);

            var action = CreateShortcutInstanceFromDescriptor(actionDescriptor);
            var actions = _bindings[gestureDescriptor];
            actions.Add(action);
        }

        public void BindActionWithSettings(IInputGestureDescriptor gestureDescriptor, IShortcutActionDescriptor actionDescriptor, IEnumerable<IShortcutSetting> settings)
        {
            if (gestureDescriptor == null || actionDescriptor == null)
                return;

            RegisterGesture(gestureDescriptor);

            var action = CreateShortcutInstanceFromDescriptor(actionDescriptor);
            PopulateShortcutInstanceWithSettings(action, settings);

            var actions = _bindings[gestureDescriptor];
            actions.Add(action);
        }

        private void PopulateShortcutInstanceWithSettings(IShortcutAction action, IEnumerable<IShortcutSetting> settings)
        {
            foreach(var (actionSetting, setting) in action.Settings.Zip(settings))
            {
                var actionSettingType = actionSetting.GetType().GetGenericArguments()[0];
                var settingType = setting.GetType().GetGenericArguments()[0];

                if (actionSettingType != settingType)
                {
                    Logger.Warn($"Action \"{action.Descriptor}\" setting type mismatch! [\"{actionSettingType}\" != \"{settingType}\"]");
                    continue;
                }

                actionSetting.Value = setting.Value;
            }
        }

        private IShortcutAction CreateShortcutInstanceFromDescriptor(IShortcutActionDescriptor actionDescriptor)
        {
            var (delegateInstance, descriptions) = _actions[actionDescriptor];
            var methodParameters = delegateInstance.GetType().GetGenericArguments();
            var settings = methodParameters[1..].Zip(descriptions, (p, d) => (Type: p, Description: d));

            var actionType = typeof(ShortcutAction);
            return (IShortcutAction)Activator.CreateInstance(actionType, new object[] { actionDescriptor, delegateInstance, settings });
        }

        public void UnbindAction(IInputGestureDescriptor gestureDescriptor, IShortcutAction action)
        {
            if (gestureDescriptor == null || action == null)
                return;
            if (!_bindings.ContainsKey(gestureDescriptor))
                return;

            var actions = _bindings[gestureDescriptor];
            actions.Remove(action);
        }

        public void RegisterGesture(IInputGestureDescriptor gestureDescriptor)
        {
            if (gestureDescriptor == null)
                return;
            if (_bindings.ContainsKey(gestureDescriptor))
                return;

            _bindings.Add(gestureDescriptor, new BindableCollection<IShortcutAction>());
        }

        public void UnregisterGesture(IInputGestureDescriptor gestureDescriptor)
        {
            if (gestureDescriptor == null)
                return;
            if (!_bindings.ContainsKey(gestureDescriptor))
                return;

            _bindings.Remove(gestureDescriptor);
        }

        private void HandleGesture(object sender, IInputGesture gesture)
        {
            var eventArgs = new GestureEventArgs(gesture);
            OnGesture?.Invoke(this, eventArgs);

            if (eventArgs.Handled)
                return;
            if (!HandleGestures)
                return;
            if (!_bindings.TryGetValue(gesture.Descriptor, out var actions))
                return;
            if (actions.Count == 0)
                return;

            Logger.Trace($"Handling {gesture.Descriptor} gesture");
            foreach (var action in actions)
            {
                Logger.Trace($"Invoking {action.Descriptor} action");
                action.Invoke(gesture);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            foreach (var processor in _processors)
                processor.OnGesture -= HandleGesture;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
