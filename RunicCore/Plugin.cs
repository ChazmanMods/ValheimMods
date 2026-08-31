using System;
using System.Collections.Generic;
using BepInEx;

namespace Runic.Foundation.Core
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "chazman.RunicCore";
        public const string Name = "Runic Core";
        public const string Version = "1.0.0";

        private readonly List<IDisposable> _registrations = new List<IDisposable>();
        private ModuleRegistration _moduleRegistration;

        private void Awake()
        {
            PluginConfig.Bind(Config);
            PluginConfig.Changed += ApplyConfiguration;
            ApplyConfiguration();

            RunicCoreApi.Notifications.Published += OnNotificationPublished;
            RunicCoreApi.Registry.ModuleChanged += OnModuleChanged;
            RunicCoreApi.Registry.CapabilityChanged += OnCapabilityChanged;
            RunicCoreApi.Keybindings.Changed += OnKeybindingsChanged;

            try
            {
                _moduleRegistration = RunicCoreApi.Registry.RegisterModule(RunicCoreApi.CoreModule);
                _registrations.Add(RunicCoreApi.Registry.RegisterService(
                    RunicCapabilityIds.FoundationModules,
                    RunicModuleIds.Core,
                    RunicCoreApi.Registry));
                _registrations.Add(RunicCoreApi.Registry.RegisterService(
                    RunicCapabilityIds.FoundationServices,
                    RunicModuleIds.Core,
                    RunicCoreApi.Registry));
                _registrations.Add(RunicCoreApi.Registry.RegisterService(
                    RunicCapabilityIds.KeybindingsRegistry,
                    RunicModuleIds.Core,
                    RunicCoreApi.Keybindings));
                _registrations.Add(RunicCoreApi.Registry.RegisterService(
                    RunicCapabilityIds.NotificationPublish,
                    RunicModuleIds.Core,
                    RunicCoreApi.Notifications));
                RunicCoreApi.SetPluginLoaded(true);
                Logger.LogInfo(
                    Name + " v" + Version + " ready (protocol " +
                    RunicCoreMetadata.ProtocolVersionText + ").");
            }
            catch (Exception exception)
            {
                DisposeRegistrations();
                _moduleRegistration?.Dispose();
                _moduleRegistration = null;
                RunicCoreApi.SetPluginLoaded(false);
                Logger.LogError(
                    "Runic Core registration failed; Valheim and other mods remain untouched. " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void OnDestroy()
        {
            RunicCoreApi.SetPluginLoaded(false);
            RunicCoreApi.Notifications.Published -= OnNotificationPublished;
            RunicCoreApi.Registry.ModuleChanged -= OnModuleChanged;
            RunicCoreApi.Registry.CapabilityChanged -= OnCapabilityChanged;
            RunicCoreApi.Keybindings.Changed -= OnKeybindingsChanged;
            PluginConfig.Changed -= ApplyConfiguration;
            PluginConfig.Unbind();
            DisposeRegistrations();
            _moduleRegistration?.Dispose();
            _moduleRegistration = null;
        }

        private void ApplyConfiguration()
        {
            RunicCoreApi.Notifications.DefaultMinimumInterval = TimeSpan.FromSeconds(
                PluginConfig.NotificationMinimumIntervalSeconds?.Value ?? 10f);
        }

        private void OnNotificationPublished(
            object sender,
            NotificationPublishedEventArgs arguments)
        {
            if (!(PluginConfig.LogPublishedNotifications?.Value ?? true)) return;
            PublishedNotification notification = arguments.Notification;
            string message = "[" + notification.SourceModuleId + "/" + notification.Code + "] " +
                             notification.Reason + " Remedy: " + notification.Remedy;
            switch (notification.Severity)
            {
                case NotificationSeverity.Error:
                    Logger.LogError(message);
                    break;
                case NotificationSeverity.Warning:
                    Logger.LogWarning(message);
                    break;
                default:
                    Logger.LogInfo(message);
                    break;
            }
        }

        private void OnModuleChanged(object sender, ModuleChangedEventArgs arguments)
        {
            if (!(PluginConfig.VerboseLogging?.Value ?? false)) return;
            Logger.LogDebug(
                "Module " + arguments.Kind + ": " + arguments.Module.ModuleId + " v" +
                arguments.Module.SemanticVersion + " (protocol " +
                arguments.Module.ProtocolVersion + ").");
        }

        private void OnCapabilityChanged(object sender, CapabilityChangedEventArgs arguments)
        {
            if (!(PluginConfig.VerboseLogging?.Value ?? false)) return;
            Logger.LogDebug(
                "Capability " + arguments.Kind + ": " + arguments.CapabilityId +
                " via " + arguments.ProviderModuleId + ", providers=" +
                arguments.Current.ProviderCount + ", services=" +
                arguments.Current.ServiceCount + ".");
        }

        private void OnKeybindingsChanged(object sender, KeybindingsChangedEventArgs arguments)
        {
            if (arguments.Conflict != null)
            {
                Logger.LogWarning(
                    "Exact input conflict on " + arguments.Conflict.Chord + ": " +
                    FormatBindings(arguments.Conflict.Bindings) +
                    ". Change one binding in its owning mod's configuration.");
                return;
            }

            if (PluginConfig.VerboseLogging?.Value ?? false)
            {
                Logger.LogDebug(
                    "Keybinding " + arguments.Kind + ": " +
                    arguments.Binding.QualifiedId + " = " + arguments.Binding.Chord + ".");
            }
        }

        private static string FormatBindings(IReadOnlyList<KeybindingDescriptor> bindings)
        {
            if (bindings.Count == 0) return "none";
            string result = bindings[0].QualifiedId;
            for (int index = 1; index < bindings.Count; index++)
                result += ", " + bindings[index].QualifiedId;
            return result;
        }

        private void DisposeRegistrations()
        {
            for (int index = _registrations.Count - 1; index >= 0; index--)
            {
                try { _registrations[index].Dispose(); }
                catch (Exception exception)
                {
                    Logger.LogWarning(
                        "A Runic Core service registration could not be released: " +
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
            _registrations.Clear();
        }
    }
}
