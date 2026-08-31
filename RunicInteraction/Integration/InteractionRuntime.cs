namespace RunicInteraction.Integration
{
    internal sealed class InteractionRuntime
    {
        private bool _dragWarning;
        private bool _filterMemoryWarning;

        internal void Initialize()
        {
            PickupFilterRuntime.Refresh();
            OnConfigurationChanged();
        }

        internal void Tick()
        {
            DoorAutoCloseRuntime.Tick();
            EquipmentRestoreRuntime.Tick();
        }

        internal void OnConfigurationChanged()
        {
            PickupFilterRuntime.Refresh();
            DoorAutoCloseRuntime.OnConfigurationChanged();
            EquipmentRestoreRuntime.OnConfigurationChanged();
            MenuMemoryRuntime.OnConfigurationChanged();
            TextEntryRuntime.OnConfigurationChanged();
            TransferGestureRuntime.OnConfigurationChanged();
            if (InteractionConfig.DragTransfer.Value && !_dragWarning)
            {
                _dragWarning = true;
                Diagnostics.Warn(
                    "DragTransfer remains disabled: Valheim 0.221.12 exposes no authority-safe " +
                    "drag-sweep transaction boundary. Ordinary vanilla dragging is unchanged.");
            }
            if (InteractionConfig.MenuMemory.Value && !_filterMemoryWarning)
            {
                _filterMemoryWarning = true;
                Diagnostics.Info(
                    "Menu memory is active for context/category/recipe selection. Filter memory is " +
                    "disabled because Valheim 0.221.12 exposes no stable filter state.");
            }
            if (!InteractionConfig.DragTransfer.Value) _dragWarning = false;
            if (!InteractionConfig.MenuMemory.Value) _filterMemoryWarning = false;
        }

        internal void Shutdown()
        {
            DoorAutoCloseRuntime.Shutdown();
            EquipmentRestoreRuntime.Shutdown();
            MenuMemoryRuntime.Shutdown();
            TextEntryRuntime.Shutdown();
            PickupFilterRuntime.Shutdown();
        }
    }
}
