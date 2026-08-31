using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using RunicAwareness.Core;
using UnityEngine;

namespace RunicAwareness.Integration
{
    internal sealed class AwarenessRuntime : IDisposable
    {
        private const int MaximumFoodRows = 3;
        private const int MaximumContextCharacters = 768;
        private readonly ManualLogSource _log;
        private readonly bool _inert;
        private readonly PanelCache _panels = new PanelCache();
        private readonly StringBuilder _builder = new StringBuilder(1024);
        private readonly OptionalPortalPanelAdapter _portalPanels =
            new OptionalPortalPanelAdapter();
        private readonly GUIContent _content = new GUIContent();
        private GUIStyle _textStyle;
        private GUIStyle _boxStyle;
        private float _nextRefresh;
        private float _nextStationLevelRefresh;
        private bool _failed;
        private bool _layoutDirty = true;
        private int _styleFontSize;
        private float _cachedHeight;
        private float _lastInnerWidth;
        private float _lastMaximumHeight;
        private int _hookFailureCount;
        private ulong _foodSignature;
        private ulong _effectSignature;
        private ulong _comfortSignature;
        private ulong _itemSignature;
        private ulong _contextSignature;
        private Piece _lastBuildingPiece;
        private int _cachedStationLevel;
        private bool _hadFoodSignature;
        private bool _hadEffectSignature;
        private bool _hadComfortSignature;
        private bool _hadItemSignature;
        private bool _hadContextSignature;

        internal AwarenessRuntime(ManualLogSource log, bool inert)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _inert = inert;
        }

        internal void Update()
        {
            if (_inert || _failed) return;
            float now = Time.unscaledTime;
            if (now < _nextRefresh && now >= 0f) return;
            _nextRefresh = now + FiniteClamp(
                AwarenessConfig.RefreshInterval.Value,
                0.2f,
                2f,
                0.35f);

            if (!AwarenessConfig.Enabled.Value)
            {
                ClearAll();
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                ClearAll();
                return;
            }

            bool inventoryVisible = InventoryGui.IsVisible();
            if (inventoryVisible)
            {
                UpdateItemComparison(player, now);
                return;
            }

            _panels.Clear(AwarenessPanel.ItemComparison);
            UpdateFood(player);
            UpdateEffects(player);
            UpdateComfort(player);
            UpdateContext(player, now);
        }

        internal void Draw()
        {
            if (_inert || _failed || !AwarenessConfig.Enabled.Value || Event.current == null ||
                Event.current.type != EventType.Repaint || ShouldSuppressOverlay())
                return;

            bool inventoryOnly = InventoryGui.IsVisible();
            if (_panels.Compose(inventoryOnly))
            {
                _content.text = _panels.ComposedText;
                _layoutDirty = true;
            }
            if (string.IsNullOrEmpty(_content.text)) return;

            bool controller = false;
            try { controller = ZInput.IsGamepadActive(); }
            catch (Exception) { }
            float scale = Mathf.Clamp(
                FiniteClamp(AwarenessConfig.UiScale.Value, 0.75f, 2f, 1f) *
                (controller
                    ? FiniteClamp(
                        AwarenessConfig.ControllerScaleMultiplier.Value,
                        1f,
                        1.5f,
                        1.15f)
                    : 1f),
                0.75f,
                3f);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(14f * scale), 11, 42);
            EnsureStyles(fontSize);

            Rect safe = Screen.safeArea;
            float margin = Mathf.Max(10f, 14f * scale);
            float maximumWidth = Mathf.Max(120f, safe.width - margin * 2f);
            float minimumWidth = Mathf.Min(280f, maximumWidth);
            float width = Mathf.Clamp(430f * scale, minimumWidth, maximumWidth);
            float innerWidth = Mathf.Max(120f, width - margin * 1.6f);
            float maximumHeight = Mathf.Max(fontSize + margin, safe.height - margin * 2f);
            if (Mathf.Abs(innerWidth - _lastInnerWidth) > 0.5f)
            {
                _lastInnerWidth = innerWidth;
                _layoutDirty = true;
            }
            if (Mathf.Abs(maximumHeight - _lastMaximumHeight) > 0.5f)
            {
                _lastMaximumHeight = maximumHeight;
                _layoutDirty = true;
            }
            if (_layoutDirty)
            {
                _cachedHeight = Mathf.Min(
                    maximumHeight,
                    _textStyle.CalcHeight(_content, innerWidth) + margin * 1.4f);
                _layoutDirty = false;
            }
            float height = Mathf.Min(
                maximumHeight,
                Mathf.Max(fontSize + margin, _cachedHeight));
            Rect panel = AnchoredRect(
                safe,
                Screen.height,
                width,
                height,
                margin,
                AwarenessConfig.Anchor.Value);
            Rect text = new Rect(
                panel.x + margin * 0.8f,
                panel.y + margin * 0.65f,
                panel.width - margin * 1.6f,
                panel.height - margin * 1.3f);
            GUI.Box(panel, GUIContent.none, _boxStyle);
            GUI.Label(text, _content, _textStyle);
        }

        internal void OnConfigurationChanged(ConfigDefinition definition)
        {
            _nextRefresh = 0f;
            ResetSignatures();
            if (definition == null ||
                (definition.Section == "Display" &&
                 definition.Key == "MaximumComfortWinnerRows"))
                ComfortCapture.Reset();
            _styleFontSize = 0;
            _layoutDirty = true;
            if (!AwarenessConfig.ShowFoodTimers.Value) _panels.Clear(AwarenessPanel.Food);
            if (!AwarenessConfig.ShowEffectTimers.Value) _panels.Clear(AwarenessPanel.Effects);
            if (!AwarenessConfig.ShowComfort.Value) _panels.Clear(AwarenessPanel.Comfort);
            if (!AwarenessConfig.ShowComfort.Value) ComfortCapture.Reset();
            if (!AwarenessConfig.ShowItemComparison.Value)
            {
                _panels.Clear(AwarenessPanel.ItemComparison);
                HoverItemCapture.Reset();
            }
            if (!AwarenessConfig.ShowProduction.Value) _panels.Clear(AwarenessPanel.Production);
            if (!AwarenessConfig.ShowAgriculture.Value) _panels.Clear(AwarenessPanel.Agriculture);
            if (!AwarenessConfig.ShowBuilding.Value) _panels.Clear(AwarenessPanel.Building);
            if (!AwarenessConfig.ShowTamedAnimals.Value) _panels.Clear(AwarenessPanel.TamedAnimal);
            if (!AwarenessConfig.ShowProduction.Value &&
                !AwarenessConfig.ShowAgriculture.Value &&
                !AwarenessConfig.ShowBuilding.Value &&
                !AwarenessConfig.ShowTamedAnimals.Value)
                ContextCapture.Reset();
            if (!AwarenessConfig.Enabled.Value)
            {
                ComfortCapture.Reset();
                HoverItemCapture.Reset();
                ContextCapture.Reset();
                ClearAll();
            }
            if (AwarenessConfig.VerboseLogging.Value)
                _log.LogInfo("Awareness configuration refreshed: " +
                             (definition == null ? "unknown" : definition.Section + "/" + definition.Key));
        }

        internal void OnLocalizationChanged()
        {
            _nextRefresh = 0f;
            ResetSignatures();
            ClearAll();
            if (AwarenessConfig.VerboseLogging.Value)
                _log.LogInfo("Awareness display cache cleared for a language change.");
        }

        internal void FailClosed(string operation, Exception exception)
        {
            if (_failed) return;
            _failed = true;
            ClearAll();
            _log.LogError(
                "Runic Awareness display runtime stopped after " + operation + "; gameplay and vanilla UI " +
                "remain unchanged. " + exception.GetType().Name + ": " + exception.Message);
        }

        internal void FailHook(string hook, Exception exception)
        {
            if (_hookFailureCount >= 3) return;
            _hookFailureCount++;
            _log.LogWarning(
                "Awareness ignored a display capture failure in " + hook + ": " +
                exception.GetType().Name + ".");
        }

        public void Dispose()
        {
            ClearAll();
            _textStyle = null;
            _boxStyle = null;
        }

        private void UpdateFood(Player player)
        {
            if (!AwarenessConfig.ShowFoodTimers.Value)
            {
                _panels.Clear(AwarenessPanel.Food);
                return;
            }
            List<Player.Food> foods = player.GetFoods();
            int count = foods?.Count ?? 0;
            ulong signature = HashStart(count, (int)AwarenessConfig.TimerPrecision.Value);
            if (count > AwarenessConfig.HardMaximumFoodScan)
            {
                signature = HashMix(signature, count);
                if (SameSignature(ref _foodSignature, ref _hadFoodSignature, signature)) return;
                _panels.Set(
                    AwarenessPanel.Food,
                    "Details withheld: local food list exceeds the hard limit of " +
                    AwarenessConfig.HardMaximumFoodScan + ".");
                return;
            }

            for (int index = 0; index < count; index++)
            {
                Player.Food food = foods[index];
                signature = HashMix(signature,
                    TimerFormatter.Bucket(food?.m_time ?? 0f, AwarenessConfig.TimerPrecision.Value));
                signature = HashMix(signature, BoundedText.HashBounded(FoodName(food), 96));
            }
            if (SameSignature(ref _foodSignature, ref _hadFoodSignature, signature)) return;
            if (count == 0)
            {
                _panels.Clear(AwarenessPanel.Food);
                return;
            }

            _builder.Clear();
            int visible = Math.Min(count, MaximumFoodRows);
            for (int index = 0; index < visible; index++)
            {
                if (index > 0) _builder.Append('\n');
                Player.Food food = foods[index];
                _builder.Append(BoundedLocalization.Label(FoodName(food))).Append(": ");
                TimerFormatter.Append(
                    _builder,
                    TimerFormatter.Bucket(food?.m_time ?? 0f, AwarenessConfig.TimerPrecision.Value));
            }
            if (count > visible)
                _builder.Append("\n+").Append(count - visible).Append(" more local food slots");
            _panels.Set(AwarenessPanel.Food,
                BoundedText.Sanitize(_builder.ToString(), 512, MaximumFoodRows + 1));
        }

        private void UpdateEffects(Player player)
        {
            if (!AwarenessConfig.ShowEffectTimers.Value)
            {
                _panels.Clear(AwarenessPanel.Effects);
                return;
            }
            List<StatusEffect> effects = player.GetSEMan()?.GetStatusEffects();
            int count = effects?.Count ?? 0;
            ulong signature = HashStart(count, (int)AwarenessConfig.TimerPrecision.Value);
            if (count > AwarenessConfig.HardMaximumEffectScan)
            {
                signature = HashMix(signature, count);
                if (SameSignature(ref _effectSignature, ref _hadEffectSignature, signature)) return;
                _panels.Set(
                    AwarenessPanel.Effects,
                    "Details withheld: local effect list exceeds the hard limit of " +
                    AwarenessConfig.HardMaximumEffectScan + ".");
                return;
            }

            int hudCount = 0;
            for (int index = 0; index < count; index++)
            {
                StatusEffect effect = effects[index];
                if (effect == null || effect.m_icon == null) continue;
                hudCount++;
                int bucket = effect.m_ttl > 0f
                    ? TimerFormatter.Bucket(effect.GetRemaningTime(), AwarenessConfig.TimerPrecision.Value)
                    : -1;
                signature = HashMix(signature, bucket);
                signature = HashMix(signature, BoundedText.HashBounded(effect.m_name, 96));
            }
            signature = HashMix(signature, hudCount);
            if (SameSignature(ref _effectSignature, ref _hadEffectSignature, signature)) return;
            if (hudCount == 0)
            {
                _panels.Clear(AwarenessPanel.Effects);
                return;
            }

            _builder.Clear();
            int maximum = Math.Max(1, Math.Min(8, AwarenessConfig.MaximumEffectRows.Value));
            int shown = 0;
            for (int index = 0; index < count && shown < maximum; index++)
            {
                StatusEffect effect = effects[index];
                if (effect == null || effect.m_icon == null) continue;
                if (shown > 0) _builder.Append('\n');
                _builder.Append(BoundedLocalization.Label(effect.m_name)).Append(": ");
                if (effect.m_ttl > 0f)
                    TimerFormatter.Append(
                        _builder,
                        TimerFormatter.Bucket(
                            effect.GetRemaningTime(), AwarenessConfig.TimerPrecision.Value));
                else
                    _builder.Append("active");
                shown++;
            }
            if (hudCount > shown)
                _builder.Append("\n+").Append(hudCount - shown).Append(" more HUD effects");
            _panels.Set(AwarenessPanel.Effects,
                BoundedText.Sanitize(_builder.ToString(), 768, maximum + 1));
        }

        private void UpdateComfort(Player player)
        {
            if (!AwarenessConfig.ShowComfort.Value)
            {
                _panels.Clear(AwarenessPanel.Comfort);
                return;
            }
            int level = Math.Max(0, player.GetComfortLevel());
            bool sheltered = player.InShelter();
            ComfortCapture.TryGet(player, level, sheltered, Time.unscaledTime,
                out CapturedComfort captured);
            StatusEffect rested = player.GetSEMan()?.GetStatusEffect(SEMan.s_statusEffectRested);
            int restedBucket = rested != null && rested.m_ttl > 0f
                ? TimerFormatter.Bucket(rested.GetRemaningTime(), AwarenessConfig.TimerPrecision.Value)
                : -1;
            ulong signature = HashStart(level, sheltered ? 1 : 0);
            signature = HashMix(signature, captured.Version);
            signature = HashMix(signature, restedBucket);
            if (SameSignature(ref _comfortSignature, ref _hadComfortSignature, signature)) return;

            _builder.Clear();
            _builder.Append("Level ").Append(level)
                .Append(sheltered ? " (sheltered)" : " (not sheltered)");
            _builder.Append("\nRested: ");
            if (restedBucket >= 0) TimerFormatter.Append(_builder, restedBucket);
            else _builder.Append("inactive");
            if (!string.IsNullOrEmpty(captured.Detail))
                _builder.Append('\n').Append(captured.Detail);
            else
                _builder.Append("\nCategory winners appear after vanilla's next comfort check.");
            _panels.Set(AwarenessPanel.Comfort,
                BoundedText.Sanitize(_builder.ToString(), 1024, 12));
        }

        private void UpdateItemComparison(Player player, float now)
        {
            if (!AwarenessConfig.ShowItemComparison.Value ||
                !HoverItemCapture.TryGet(player, now, out ItemDrop.ItemData selected) ||
                selected?.m_shared == null)
            {
                _panels.Clear(AwarenessPanel.ItemComparison);
                _hadItemSignature = false;
                return;
            }
            string itemType = selected.m_shared.m_itemType.ToString();
            ItemComparisonDisposition disposition = ItemTypePolicy.Classify(itemType);
            if (disposition == ItemComparisonDisposition.HideKnownNonEquipment)
            {
                _panels.Clear(AwarenessPanel.ItemComparison);
                _hadItemSignature = false;
                return;
            }
            try
            {
                ItemDrop.ItemData equipped =
                    disposition == ItemComparisonDisposition.CompareKnownEquipment
                        ? ValheimContracts.EquippedFor(player, selected)
                        : null;
                ItemMetrics selectedMetrics = ReadMetrics(player, selected, itemType);
                ItemMetrics? equippedMetrics = equipped?.m_shared == null
                    ? (ItemMetrics?)null
                    : ReadMetrics(player, equipped, equipped.m_shared.m_itemType.ToString());
                ulong signature = ItemSignature(
                    selected,
                    equipped,
                    selectedMetrics,
                    equippedMetrics);
                if (SameSignature(ref _itemSignature, ref _hadItemSignature, signature)) return;
                _panels.Set(
                    AwarenessPanel.ItemComparison,
                    ItemComparisonFormatter.Format(
                        selectedMetrics,
                        equippedMetrics,
                        ReferenceEquals(selected, equipped)));
            }
            catch (Exception exception)
            {
                _panels.Clear(AwarenessPanel.ItemComparison);
                _hadItemSignature = false;
                FailHook("item comparison", exception);
            }
        }

        private void UpdateContext(Player player, float now)
        {
            Piece buildingPiece = AwarenessConfig.ShowBuilding.Value
                ? player.GetHoveringPiece()
                : null;
            if (buildingPiece != null)
            {
                ClearContextExcept(AwarenessPanel.Building);
                if (AllowsDetailedDisclosure(player, buildingPiece))
                    UpdateBuilding(buildingPiece, now);
                else
                    UpdateBuildingUnavailable();
                return;
            }

            GameObject hover = player.GetHoverObject();
            if (!ContextCapture.TryGet(hover, now, out CapturedContext context) ||
                !ContextEnabled(context.Kind))
            {
                ClearContextPanels();
                if (_hadContextSignature)
                {
                    _hadContextSignature = false;
                    _contextSignature = 0UL;
                }
                return;
            }

            AwarenessPanel targetPanel = PanelFor(context.Kind);
            ClearContextExcept(targetPanel);

            ulong signature = HashStart((int)context.Kind,
                context.Subject == null ? 0 : context.Subject.GetInstanceID());
            signature = HashMix(signature,
                BoundedText.HashBounded(context.VisibleText, MaximumContextCharacters));
            if (SameSignature(ref _contextSignature, ref _hadContextSignature, signature)) return;

            string visible = BoundedText.Sanitize(
                context.VisibleText,
                MaximumContextCharacters,
                Math.Max(1, Math.Min(6, AwarenessConfig.MaximumContextLines.Value)));
            string body = visible;
            if (string.IsNullOrEmpty(body))
            {
                _panels.Clear(targetPanel);
                return;
            }
            _panels.Set(targetPanel, body);
        }

        private void UpdateBuilding(Piece piece, float now)
        {
            WearNTear wear = piece.GetComponentInParent<WearNTear>();
            float health = wear == null ? -1f : wear.GetHealthPercentage() * 100f;
            CraftingStation station = piece.m_craftingStation;
            if (!ReferenceEquals(piece, _lastBuildingPiece) || now >= _nextStationLevelRefresh)
            {
                _lastBuildingPiece = piece;
                _cachedStationLevel = station == null ? 0 : Math.Max(0, station.GetLevel(false));
                _nextStationLevelRefresh = now + 2f;
            }
            Vector3 position = piece.transform.position;
            Vector3 rotation = piece.transform.eulerAngles;
            ulong signature = HashStart(piece.GetInstanceID(), Quantize(health));
            signature = HashMix(signature, Quantize(position.x));
            signature = HashMix(signature, Quantize(position.y));
            signature = HashMix(signature, Quantize(position.z));
            signature = HashMix(signature, Quantize(rotation.x));
            signature = HashMix(signature, Quantize(rotation.y));
            signature = HashMix(signature, Quantize(rotation.z));
            signature = HashMix(signature, _cachedStationLevel);
            if (SameSignature(ref _contextSignature, ref _hadContextSignature, signature)) return;

            _builder.Clear();
            _builder.Append(BoundedLocalization.Label(piece.m_name));
            if (health >= 0f)
                _builder.Append(" | health ").Append(health.ToString("0", CultureInfo.InvariantCulture))
                    .Append('%');
            _builder.Append("\nPosition: ").Append(VectorText(position));
            _builder.Append("\nRotation: ").Append(VectorText(rotation));
            if (_cachedStationLevel > 0)
                _builder.Append("\nStation level: ").Append(_cachedStationLevel);
            _builder.Append("\nSupport colors: blue grounded; green/yellow/orange supported; red weakest.");
            _panels.Set(AwarenessPanel.Building,
                BoundedText.Sanitize(_builder.ToString(), 768, 6));
        }

        private void UpdateBuildingUnavailable()
        {
            const string message =
                "Building details unavailable outside avatar interaction reach or without current ward access.";
            ulong signature = HashStart((int)AwarenessPanel.Building, 0x554E4156);
            if (SameSignature(ref _contextSignature, ref _hadContextSignature, signature)) return;
            _lastBuildingPiece = null;
            _cachedStationLevel = 0;
            _nextStationLevelRefresh = 0f;
            _panels.Set(AwarenessPanel.Building, message);
        }

        private static ItemMetrics ReadMetrics(
            Player player,
            ItemDrop.ItemData item,
            string itemType)
        {
            Skills.SkillType skill = item.m_shared.m_skillType;
            float skillFactor = player.GetSkillFactor(skill);
            float skillLevel = player.GetSkillLevel(skill);
            return new ItemMetrics(
                BoundedLocalization.Label(item.m_shared.m_name),
                itemType,
                item.m_quality,
                item.GetDamage().GetTotalDamage(),
                item.GetArmor(),
                item.GetBlockPower(skillFactor),
                item.m_shared.m_movementModifier * 100f,
                item.GetNonStackedWeight(),
                item.m_durability,
                item.GetMaxDurability(),
                skill.ToString(),
                skillLevel);
        }

        private static ulong ItemSignature(
            ItemDrop.ItemData selected,
            ItemDrop.ItemData equipped,
            ItemMetrics selectedMetrics,
            ItemMetrics? equippedMetrics)
        {
            ulong signature = HashStart(
                RuntimeHelpers.GetHashCode(selected),
                equipped == null ? 0 : RuntimeHelpers.GetHashCode(equipped));
            signature = HashMetrics(signature, selectedMetrics);
            if (equippedMetrics.HasValue)
            {
                signature = HashMix(signature, 1);
                signature = HashMetrics(signature, equippedMetrics.Value);
            }
            return signature;
        }

        private static ulong HashMetrics(ulong signature, ItemMetrics metrics)
        {
            signature = HashMix(signature, BoundedText.HashBounded(metrics.Name, 96));
            signature = HashMix(signature, BoundedText.HashBounded(metrics.ItemType, 96));
            signature = HashMix(signature, metrics.Quality);
            signature = HashMix(signature, Quantize(metrics.Damage));
            signature = HashMix(signature, Quantize(metrics.Armor));
            signature = HashMix(signature, Quantize(metrics.Block));
            signature = HashMix(signature, Quantize(metrics.MovementPercent));
            signature = HashMix(signature, Quantize(metrics.Weight));
            signature = HashMix(signature, Quantize(metrics.Durability));
            signature = HashMix(signature, Quantize(metrics.MaximumDurability));
            signature = HashMix(signature, BoundedText.HashBounded(metrics.SkillName, 96));
            return HashMix(signature, Quantize(metrics.SkillLevel));
        }

        private static string FoodName(Player.Food food) =>
            food?.m_item?.m_shared?.m_name ?? food?.m_name ?? string.Empty;

        private static bool AllowsDetailedDisclosure(Player player, Component subject)
        {
            if (player == null || subject == null) return false;
            Transform subjectTransform = subject.transform;
            Transform playerTransform = player.transform;
            if (subjectTransform == null || playerTransform == null) return false;
            Vector3 subjectPosition = subjectTransform.position;
            return ContextDisclosurePolicy.AllowsDetailedDisclosure(
                (subjectPosition - playerTransform.position).sqrMagnitude,
                player.m_maxInteractDistance) &&
                   StrictWardDisclosure.Allows(subjectPosition);
        }

        private bool ContextEnabled(AwarenessContextKind kind)
        {
            switch (kind)
            {
                case AwarenessContextKind.Production: return AwarenessConfig.ShowProduction.Value;
                case AwarenessContextKind.Agriculture: return AwarenessConfig.ShowAgriculture.Value;
                case AwarenessContextKind.Building: return AwarenessConfig.ShowBuilding.Value;
                case AwarenessContextKind.TamedAnimal: return AwarenessConfig.ShowTamedAnimals.Value;
                default: return false;
            }
        }

        private static AwarenessPanel PanelFor(AwarenessContextKind kind)
        {
            switch (kind)
            {
                case AwarenessContextKind.Production: return AwarenessPanel.Production;
                case AwarenessContextKind.Agriculture: return AwarenessPanel.Agriculture;
                case AwarenessContextKind.Building: return AwarenessPanel.Building;
                case AwarenessContextKind.TamedAnimal: return AwarenessPanel.TamedAnimal;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private void ClearContextPanels()
        {
            _panels.Clear(AwarenessPanel.Production);
            _panels.Clear(AwarenessPanel.Agriculture);
            _panels.Clear(AwarenessPanel.Building);
            _panels.Clear(AwarenessPanel.TamedAnimal);
        }

        private void ClearContextExcept(AwarenessPanel retained)
        {
            if (retained != AwarenessPanel.Production) _panels.Clear(AwarenessPanel.Production);
            if (retained != AwarenessPanel.Agriculture) _panels.Clear(AwarenessPanel.Agriculture);
            if (retained != AwarenessPanel.Building) _panels.Clear(AwarenessPanel.Building);
            if (retained != AwarenessPanel.TamedAnimal) _panels.Clear(AwarenessPanel.TamedAnimal);
        }

        private void ClearAll()
        {
            for (int index = 0; index <= (int)AwarenessPanel.TamedAnimal; index++)
                _panels.Clear((AwarenessPanel)index);
            _content.text = string.Empty;
            _layoutDirty = true;
            ResetSignatures();
            _lastBuildingPiece = null;
            _cachedStationLevel = 0;
            _nextStationLevelRefresh = 0f;
        }

        private void ResetSignatures()
        {
            _foodSignature = _effectSignature = _comfortSignature = _itemSignature =
                _contextSignature = 0UL;
            _hadFoodSignature = _hadEffectSignature = _hadComfortSignature =
                _hadItemSignature = _hadContextSignature = false;
        }

        private bool ShouldSuppressOverlay()
        {
            Player player = Player.m_localPlayer;
            if (player == null || Menu.IsVisible() || global::Console.IsVisible() ||
                TextInput.IsVisible() || StoreGui.IsVisible() || InventoryGui.IsVisible() ||
                Minimap.IsOpen() || Hud.IsPieceSelectionVisible() ||
                PlayerCustomizaton.IsBarberGuiVisible() || Feedback.IsVisible() ||
                UnifiedPopup.IsVisible() || ConnectPanel.IsVisible() ||
                ZInput.VirtualKeyboardOpen)
                return true;
            TextViewer textViewer = TextViewer.instance;
            if (textViewer != null && textViewer.IsVisible()) return true;
            Chat chat = Chat.instance;
            if (chat != null && (chat.HasFocus() || chat.IsChatDialogWindowVisible()))
                return true;
            if (Game.IsPaused() || Hud.instance == null || Hud.instance.m_userHidden)
                return true;
            return _portalPanels.SetupPanelVisible(player);
        }

        private void EnsureStyles(int fontSize)
        {
            if (_textStyle != null && _boxStyle != null && _styleFontSize == fontSize) return;
            _textStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _textStyle.normal.textColor = new Color(0.92f, 0.95f, 1f, 1f);
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.textColor = Color.clear;
            _styleFontSize = fontSize;
            _lastInnerWidth = 0f;
            _lastMaximumHeight = 0f;
            _layoutDirty = true;
        }

        internal static Rect AnchoredRect(
            Rect safe,
            float screenHeight,
            float width,
            float height,
            float margin,
            AwarenessOverlayAnchor anchor)
        {
            bool middleLeft = anchor == AwarenessOverlayAnchor.MiddleLeft;
            bool right = anchor == AwarenessOverlayAnchor.TopRight ||
                         anchor == AwarenessOverlayAnchor.BottomRight;
            bool bottom = anchor == AwarenessOverlayAnchor.BottomRight ||
                          anchor == AwarenessOverlayAnchor.BottomLeft;
            float x = right
                ? safe.xMax - width - margin
                : safe.xMin + margin;
            float safeTop = screenHeight - safe.yMax;
            float safeBottom = screenHeight - safe.yMin;
            float y = middleLeft
                ? safeTop + (safe.height - height) * 0.5f
                : bottom
                    ? safeBottom - height - margin
                    : safeTop + margin;
            return new Rect(x, y, width, height);
        }

        private static bool SameSignature(ref ulong stored, ref bool hasStored, ulong current)
        {
            if (hasStored && stored == current) return true;
            stored = current;
            hasStored = true;
            return false;
        }

        private static ulong HashStart(int left, int right)
        {
            ulong hash = 1469598103934665603UL;
            hash = HashMix(hash, left);
            return HashMix(hash, right);
        }

        private static ulong HashMix(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                return hash * 1099511628211UL;
            }
        }

        private static ulong HashMix(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 1099511628211UL;
            }
        }

        private static int Quantize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            double scaled = Math.Round(value * 10d, MidpointRounding.AwayFromZero);
            return scaled > int.MaxValue ? int.MaxValue :
                scaled < int.MinValue ? int.MinValue : (int)scaled;
        }

        private static float FiniteClamp(float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static string VectorText(Vector3 value) =>
            value.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
            value.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
            value.z.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
