using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.GUI;
using Jotunn.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Jotunn.Managers;

/// <summary>
///     Manager for handling anything GUI related. Provides Valheim style 
///     GUI elements as well as an anchor for custom GUI prefabs.
/// </summary>
public class GUIManager : IManager
{
	private static class Patches
	{
		[HarmonyPostfix]
		[HarmonyPatch(typeof(PlayerController), "TakeInput")]
		[HarmonyPatch(typeof(Player), "TakeInput")]
		private static void TakeInputPatch(ref bool __result)
		{
			TakeInput(ref __result);
		}

		[HarmonyPatch(typeof(TextInput), "IsVisible")]
		[HarmonyPostfix]
		private static void TextInputPatch(ref bool __result)
		{
			TextInput_IsVisible(ref __result);
		}

		[HarmonyPatch(typeof(InventoryGui), "Update")]
		[HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
		[HarmonyTranspiler]
		[HarmonyWrapSafe]
		private static IEnumerable<CodeInstruction> BlockUsePatch(IEnumerable<CodeInstruction> instructions)
		{
			return BlockUseTranspiler(instructions);
		}

		[HarmonyPatch(typeof(FejdStartup), "SetupGui")]
		[HarmonyPatch(typeof(Game), "Start")]
		[HarmonyPostfix]
		private static void CreateCustomGUI()
		{
			if (!Instance.TryCreateGUI())
			{
				Logger.LogError("Could not create custom GUI");
			}
		}
	}

	private static GUIManager _instance;

	private bool hasInitializedAssets;

	/// <summary>
	///     Unity layer constant for UI objects.
	/// </summary>
	public const int UILayer = 5;

	/// <summary>
	///     The default Valheim beige color.
	/// </summary>
	public Color ValheimBeige = new Color(0.8529f, 0.725f, 0.5331f, 1f);

	/// <summary>
	///     The default Valheim orange color.
	/// </summary>
	public Color ValheimOrange = new Color(1f, 0.631f, 0.235f, 1f);

	/// <summary>
	///     The default Valheim yellow color.
	/// </summary>
	public Color ValheimYellow = new Color(1f, 0.889f, 0f, 1f);

	/// <summary>
	///     Scrollbar handle color block in default Valheim orange.
	/// </summary>
	public ColorBlock ValheimScrollbarHandleColorBlock;

	/// <summary>
	///     Toggle color block in Valheim style.
	/// </summary>
	public ColorBlock ValheimToggleColorBlock;

	/// <summary>
	///     Button color block in Valheim style
	/// </summary>
	public ColorBlock ValheimButtonColorBlock;

	/// <summary>
	///     <see cref="T:UnityEngine.UI.DefaultControls.Resources" /> with default Valheim assets.
	/// </summary>
	public Resources ValheimControlResources;

	/// <summary>
	///     SpriteAtlas holding the references to the sprites used in the helper methods.
	/// </summary>
	private SpriteAtlas UIAtlas;

	/// <summary>
	///     SpriteAtlas holding the references to the sprites used in the helper methods.
	/// </summary>
	private SpriteAtlas IconAtlas;

	/// <summary>
	///     Indicates if the PixelFix must be created for the start or main scene.
	/// </summary>
	private bool GUIInStart;

	/// <summary>
	///     Global indicator if the input is currently blocked by the GUIManager.
	/// </summary>
	internal static bool InputBlocked;

	/// <summary>
	///     Counter to track multiple block requests.
	/// </summary>
	private static int InputBlockRequests;

	/// <summary>
	///     Singleton instance
	/// </summary>
	public static GUIManager Instance => _instance ?? (_instance = new GUIManager());

	/// <summary>
	///     GUI container with automatic scaling for high res displays.
	///     Gets rebuild at every scene change so make sure to add your custom
	///     GUI prefabs again on each scene change.
	/// </summary>
	[Obsolete("Use CustomGUIFront or CustomGUIBack")]
	public static GameObject PixelFix { get; private set; }

	/// <summary>
	///     GUI container in front of Valheim's GUI elements with automatic scaling for
	///     high res displays and pixel correction.
	///     Gets rebuild at every scene change so make sure to add your custom
	///     GUI prefabs again on each scene change.
	/// </summary>
	public static GameObject CustomGUIFront { get; private set; }

	/// <summary>
	///     GUI container behind Valheim's GUI elements with automatic scaling for
	///     high res displays and pixel correction.
	///     Gets rebuild at every scene change so make sure to add your custom
	///     GUI prefabs again on each scene change.
	/// </summary>
	public static GameObject CustomGUIBack { get; private set; }

	/// <summary>
	///     Valheim's standard font, normal faced.
	/// </summary>
	public Font AveriaSerif { get; private set; }

	/// <summary>
	///     Valheim's standard font, bold faced.
	/// </summary>
	public Font AveriaSerifBold { get; private set; }

	/// <summary>
	///     Valheim's rune-like font, normal faced.
	/// </summary>
	public Font Norse { get; private set; }

	/// <summary>
	///     Valheim's rune-like font, bold faced.
	/// </summary>
	public Font NorseBold { get; private set; }

	/// <summary>
	///     Valheim's standard font as a TMPro FontAsset
	/// </summary>
	public TMP_FontAsset TMP_AveriaSansLibre { get; private set; }

	/// <summary>
	///     Valheim's rune-like font as a TMPro FontAsset
	/// </summary>
	public TMP_FontAsset TMP_Norse { get; private set; }

	/// <summary>
	///     Event that gets fired every time the Unity scene changed and a new PixelFix
	///     object was created. Subscribe to this event to create your custom GUI objects
	///     and add them as a child to the <see cref="P:Jotunn.Managers.GUIManager.PixelFix" />.
	/// </summary>
	[Obsolete("Use OnCustomGUIAvailable")]
	public static event Action OnPixelFixCreated;

	/// <summary>
	///     Event that gets fired every time the Unity scene changed and new CustomGUI 
	///     objects were created. Subscribe to this event to create your custom GUI objects
	///     and add them as a child to either <see cref="P:Jotunn.Managers.GUIManager.CustomGUIFront" /> or <see cref="P:Jotunn.Managers.GUIManager.CustomGUIBack" />.
	/// </summary>
	public static event Action OnCustomGUIAvailable;

	/// <summary>
	///     Hide .ctor
	/// </summary>
	private GUIManager()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_024a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_028a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		ColorBlock val = default(ColorBlock);
		((ColorBlock)(ref val)).normalColor = new Color(0.926f, 0.645f, 0.34f, 1f);
		((ColorBlock)(ref val)).highlightedColor = new Color(1f, 0.786f, 0.088f, 1f);
		((ColorBlock)(ref val)).pressedColor = new Color(0.838f, 0.647f, 0.03f, 1f);
		((ColorBlock)(ref val)).selectedColor = new Color(1f, 0.786f, 0.088f, 1f);
		((ColorBlock)(ref val)).disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.502f);
		((ColorBlock)(ref val)).colorMultiplier = 1f;
		((ColorBlock)(ref val)).fadeDuration = 0.1f;
		ValheimScrollbarHandleColorBlock = val;
		val = default(ColorBlock);
		((ColorBlock)(ref val)).normalColor = new Color(0.61f, 0.61f, 0.61f, 1f);
		((ColorBlock)(ref val)).highlightedColor = new Color(1f, 1f, 1f, 1f);
		((ColorBlock)(ref val)).pressedColor = new Color(0.784f, 0.784f, 0.784f, 1f);
		((ColorBlock)(ref val)).selectedColor = new Color(1f, 1f, 1f, 1f);
		((ColorBlock)(ref val)).disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.502f);
		((ColorBlock)(ref val)).colorMultiplier = 1f;
		((ColorBlock)(ref val)).fadeDuration = 0.1f;
		ValheimToggleColorBlock = val;
		val = default(ColorBlock);
		((ColorBlock)(ref val)).normalColor = new Color(0.824f, 0.824f, 0.824f, 1f);
		((ColorBlock)(ref val)).highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
		((ColorBlock)(ref val)).pressedColor = new Color(0.537f, 0.556f, 0.556f, 1f);
		((ColorBlock)(ref val)).selectedColor = new Color(0.824f, 0.824f, 0.824f, 1f);
		((ColorBlock)(ref val)).disabledColor = new Color(0.566f, 0.566f, 0.566f, 0.502f);
		((ColorBlock)(ref val)).colorMultiplier = 1f;
		((ColorBlock)(ref val)).fadeDuration = 0.1f;
		ValheimButtonColorBlock = val;
		base..ctor();
	}

	static GUIManager()
	{
		((IManager)Instance).Init();
	}

	/// <summary>
	///     Detect headless mode (aka dedicated server)
	/// </summary>
	/// <returns></returns>
	public static bool IsHeadless()
	{
		return GUIUtils.IsHeadless;
	}

	/// <summary>
	///     Block all input except GUI
	/// </summary>
	/// <param name="state">Indicator if the input should be blocked or released</param>
	public static void BlockInput(bool state)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		if (IsHeadless())
		{
			return;
		}
		Scene activeScene = SceneManager.GetActiveScene();
		if (((Scene)(ref activeScene)).name == "main")
		{
			if (state)
			{
				InputBlockRequests++;
			}
			else
			{
				InputBlockRequests = Math.Max(--InputBlockRequests, 0);
			}
			if (!InputBlocked && InputBlockRequests > 0)
			{
				EnableInputBlock();
			}
			if (InputBlocked && InputBlockRequests == 0)
			{
				ResetInputBlock();
			}
		}
	}

	private static bool IsInputBlocked()
	{
		return InputBlocked;
	}

	/// <summary>
	///     Enable the InputBlock
	/// </summary>
	private static void EnableInputBlock()
	{
		InputBlocked = true;
		if (Object.op_Implicit((Object)(object)GameCamera.instance))
		{
			GameCamera.instance.m_mouseCapture = false;
			GameCamera.instance.UpdateMouseCapture();
		}
	}

	/// <summary>
	///     Reset the InputBlock to its initial state (disabled)
	/// </summary>
	private static void ResetInputBlock()
	{
		InputBlocked = false;
		InputBlockRequests = 0;
		if (Object.op_Implicit((Object)(object)GameCamera.instance))
		{
			GameCamera.instance.m_mouseCapture = true;
			GameCamera.instance.UpdateMouseCapture();
		}
	}

	private static void TakeInput(ref bool __result)
	{
		if (InputBlocked)
		{
			__result = false;
		}
	}

	private static void TextInput_IsVisible(ref bool __result)
	{
		if (InputBlocked)
		{
			__result = true;
		}
	}

	/// <summary>
	///     Initialize the manager
	/// </summary>
	void IManager.Init()
	{
		Main.LogInit("GUIManager");
		if (!IsHeadless())
		{
			Instance.TryCreateGUI();
			Main.Harmony.PatchAll(typeof(Patches));
			InitializeAssets();
			SceneManager.sceneLoaded += delegate
			{
				InitializeAssets();
			};
			SceneManager.sceneLoaded += delegate
			{
				Instance.TryCreateGUI();
			};
		}
	}

	/// <summary>
	///     Prevents hiding the InventoryGui on an E press or similar when the GUIManager blocks the input
	/// </summary>
	/// <param name="instructions"></param>
	/// <returns></returns>
	private static IEnumerable<CodeInstruction> BlockUseTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected O, but got Unknown
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Expected O, but got Unknown
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected O, but got Unknown
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Expected O, but got Unknown
		Label? jumpLabel = null;
		return new CodeMatcher(instructions, (ILGenerator)null).MatchForward(true, (CodeMatch[])(object)new CodeMatch[2]
		{
			new CodeMatch((Func<CodeInstruction, bool>)((CodeInstruction i) => CodeInstructionExtensions.Calls(i, AccessTools.Method(typeof(Menu), "IsVisible", (Type[])null, (Type[])null))), (string)null),
			new CodeMatch((Func<CodeInstruction, bool>)((CodeInstruction i) => CodeInstructionExtensions.Branches(i, ref jumpLabel)), (string)null)
		}).Advance(1).InsertAndAdvance((CodeInstruction[])(object)new CodeInstruction[2]
		{
			new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(GUIManager), "IsInputBlocked", (Type[])null, (Type[])null)),
			new CodeInstruction(OpCodes.Brtrue, (object)jumpLabel)
		})
			.InstructionEnumeration();
	}

	/// <summary>
	///     Load GUI assets on first start
	/// </summary>
	internal void InitializeAssets()
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ce: Unknown result type (might be due to invalid IL or missing references)
		if (hasInitializedAssets)
		{
			return;
		}
		Scene activeScene = SceneManager.GetActiveScene();
		if (!(((Scene)(ref activeScene)).name == "start") && !(((Scene)(ref activeScene)).name == "main"))
		{
			return;
		}
		try
		{
			UIAtlas = PrefabManager.Cache.GetPrefab<SpriteAtlas>("UIAtlas");
			IconAtlas = PrefabManager.Cache.GetPrefab<SpriteAtlas>("IconAtlas");
			AssertMissingAsset((Object)(object)UIAtlas, "UIAtlas", "SpriteAtlas");
			AssertMissingAsset((Object)(object)IconAtlas, "IconAtlas", "SpriteAtlas");
			AveriaSerif = PrefabManager.Cache.GetPrefab<Font>("AveriaSerifLibre-Regular");
			AveriaSerifBold = PrefabManager.Cache.GetPrefab<Font>("AveriaSerifLibre-Bold");
			Norse = PrefabManager.Cache.GetPrefab<Font>("Norse");
			NorseBold = PrefabManager.Cache.GetPrefab<Font>("Norsebold");
			TMP_AveriaSansLibre = PrefabManager.Cache.GetPrefab<TMP_FontAsset>("Valheim-AveriaSansLibre");
			TMP_Norse = PrefabManager.Cache.GetPrefab<TMP_FontAsset>("Valheim-Norse");
			AssertMissingAsset((Object)(object)AveriaSerif, "AveriaSerif", "Font");
			AssertMissingAsset((Object)(object)AveriaSerifBold, "AveriaSerifBold", "Font");
			AssertMissingAsset((Object)(object)Norse, "Norse", "Font");
			AssertMissingAsset((Object)(object)NorseBold, "NorseBold", "Font");
			AssertMissingAsset((Object)(object)TMP_AveriaSansLibre, "TMP_AveriaSansLibre", "TMP_FontAsset");
			AssertMissingAsset((Object)(object)TMP_Norse, "TMP_Norse", "TMP_FontAsset");
			AssetBundle val = AssetUtils.LoadAssetBundleFromResources("jotunn", typeof(Main).Assembly);
			GameObject val2 = val.LoadAsset<GameObject>("UIMaskStub");
			ValheimControlResources.standard = GetSprite("button");
			ValheimControlResources.background = GetSprite("text_field");
			ValheimControlResources.inputField = GetSprite("text_field");
			ValheimControlResources.knob = GetSprite("checkbox_marker");
			ValheimControlResources.checkmark = GetSprite("checkbox_marker");
			ValheimControlResources.dropdown = GetSprite("checkbox_marker");
			ValheimControlResources.mask = val2.GetComponent<Image>().sprite;
			val.Unload(false);
			AssetBundle val3 = AssetUtils.LoadAssetBundleFromResources("colorpicker", typeof(Main).Assembly);
			GameObject val4 = val3.LoadAsset<GameObject>("ColorPicker");
			Image component = val4.GetComponent<Image>();
			component.sprite = GetSprite("woodpanel_settings");
			component.type = (Type)1;
			component.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
			((Graphic)component).material = PrefabManager.Cache.GetPrefab<Material>("litpanel");
			Text[] componentsInChildren = val4.GetComponentsInChildren<Text>(true);
			foreach (Text val5 in componentsInChildren)
			{
				ApplyTextStyle(val5, ValheimOrange, val5.fontSize);
			}
			InputField[] componentsInChildren2 = val4.GetComponentsInChildren<InputField>(true);
			foreach (InputField field in componentsInChildren2)
			{
				ApplyInputFieldStyle(field, 13);
			}
			Button[] componentsInChildren3 = val4.GetComponentsInChildren<Button>(true);
			foreach (Button button in componentsInChildren3)
			{
				ApplyButtonStyle(button, 13);
			}
			PrefabManager.Instance.AddPrefab(val4, ((BaseUnityPlugin)Main.Instance).Info.Metadata);
			GameObject val6 = val3.LoadAsset<GameObject>("GradientPicker");
			Image component2 = val6.GetComponent<Image>();
			component2.sprite = GetSprite("woodpanel_settings");
			component2.type = (Type)1;
			component2.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
			((Graphic)component2).material = PrefabManager.Cache.GetPrefab<Material>("litpanel");
			Text[] componentsInChildren4 = val6.GetComponentsInChildren<Text>(true);
			foreach (Text val7 in componentsInChildren4)
			{
				ApplyTextStyle(val7, ValheimOrange, val7.fontSize);
			}
			InputField[] componentsInChildren5 = val6.GetComponentsInChildren<InputField>(true);
			foreach (InputField field2 in componentsInChildren5)
			{
				ApplyInputFieldStyle(field2, 13);
			}
			Button[] componentsInChildren6 = val6.GetComponentsInChildren<Button>(true);
			foreach (Button val8 in componentsInChildren6)
			{
				if (((Object)val8).name != "ColorButton")
				{
					ApplyButtonStyle(val8, 13);
				}
			}
			PrefabManager.Instance.AddPrefab(val6, ((BaseUnityPlugin)Main.Instance).Info.Metadata);
			val3.Unload(false);
		}
		catch (Exception data)
		{
			Logger.LogError(data);
		}
		finally
		{
			hasInitializedAssets = true;
		}
	}

	private void AssertMissingAsset(Object asset, string name, string type)
	{
		if (!Object.op_Implicit(asset))
		{
			Logger.LogWarning(name + " (" + type + ") not found");
		}
	}

	private bool TryCreateGUI()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		Scene activeScene = SceneManager.GetActiveScene();
		GUIInStart = ((Scene)(ref activeScene)).name == "start";
		ResetInputBlock();
		if (!((Scene)(ref activeScene)).isLoaded)
		{
			return false;
		}
		if (Object.op_Implicit((Object)(object)CustomGUIFront) && Object.op_Implicit((Object)(object)CustomGUIBack))
		{
			return true;
		}
		Transform val = FindGUIRoot();
		if (Object.op_Implicit((Object)(object)val))
		{
			CustomGUIFront = CreateCustomGUI("CustomGUIFront", 2000, val);
			CustomGUIFront.transform.SetAsLastSibling();
			CustomGUIBack = CreateCustomGUI("CustomGUIBack", 0, val);
			CustomGUIBack.transform.SetAsFirstSibling();
			PixelFix = CustomGUIFront;
			InvokeOnPixelFixCreated();
			InvokeOnCustomGUIAvailable();
			return true;
		}
		return false;
	}

	private static Transform FindGUIRoot()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		Scene activeScene = SceneManager.GetActiveScene();
		GameObject[] rootGameObjects = ((Scene)(ref activeScene)).GetRootGameObjects();
		GameObject[] array = rootGameObjects;
		foreach (GameObject val in array)
		{
			string name = ((Object)val).name;
			if (name == "GuiRoot")
			{
				return val.transform.Find("GUI");
			}
			if (name == "_GameMain")
			{
				return val.transform.Find("LoadingGUI");
			}
		}
		return null;
	}

	/// <summary>
	///     Create GameObjects for mods to append their custom GUI to
	/// </summary>
	/// <param name="name"></param>
	/// <param name="sortingOrder"></param>
	/// <param name="parent"></param>
	private GameObject CreateCustomGUI(string name, int sortingOrder, Transform parent)
	{
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = new GameObject(name, new Type[5]
		{
			typeof(RectTransform),
			typeof(GuiPixelFix),
			typeof(Canvas),
			typeof(CanvasScaler),
			typeof(GraphicRaycaster)
		});
		val.layer = 5;
		val.transform.SetParent(((Component)parent).transform, false);
		RectTransform component = val.GetComponent<RectTransform>();
		component.anchorMin = Vector2.zero;
		component.anchorMax = Vector2.one;
		component.offsetMin = Vector2.zero;
		component.offsetMax = Vector2.zero;
		component.anchoredPosition = Vector2.zero;
		Canvas component2 = val.GetComponent<Canvas>();
		component2.additionalShaderChannels = (AdditionalCanvasShaderChannels)25;
		component2.renderMode = (RenderMode)0;
		component2.overrideSorting = true;
		component2.sortingOrder = sortingOrder;
		CanvasScaler component3 = val.GetComponent<CanvasScaler>();
		component3.referencePixelsPerUnit = 50f;
		return val;
	}

	[Obsolete]
	private void InvokeOnPixelFixCreated()
	{
		GUIManager.OnPixelFixCreated?.SafeInvoke();
	}

	private void InvokeOnCustomGUIAvailable()
	{
		GUIManager.OnCustomGUIAvailable?.SafeInvoke();
	}

	/// <summary>
	///     Add a <see cref="T:Jotunn.Configs.KeyHintConfig" /> to the manager.<br />
	///     Checks if the custom key hint is unique (i.e. the first one registered for an item).<br />
	///     Custom status effects are displayed in the game instead of the default 
	///     KeyHints for equipped tools or weapons they are registered for.
	/// </summary>
	/// <param name="hintConfig">The custom key hint config to add.</param>
	/// <returns>true if the custom key hint config was added to the manager.</returns>
	[Obsolete("Use KeyHintManager.AddKeyHint instead")]
	public bool AddKeyHint(KeyHintConfig hintConfig)
	{
		return KeyHintManager.Instance.AddKeyHint(hintConfig);
	}

	/// <summary>
	///     Removes a <see cref="T:Jotunn.Configs.KeyHintConfig" /> from the game.
	/// </summary>
	/// <param name="hintConfig">The custom key hint config to add.</param>
	[Obsolete("Use KeyHintManager.RemoveKeyHint instead")]
	public void RemoveKeyHint(KeyHintConfig hintConfig)
	{
		KeyHintManager.Instance.RemoveKeyHint(hintConfig);
	}

	/// <summary>
	///     Get a sprite by name.
	/// </summary>
	/// <param name="spriteName">The sprite name</param>
	/// <returns>The sprite with given name</returns>
	public Sprite GetSprite(string spriteName)
	{
		SpriteAtlas uIAtlas = UIAtlas;
		Sprite val = ((uIAtlas != null) ? uIAtlas.GetSprite(spriteName) : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		SpriteAtlas iconAtlas = IconAtlas;
		val = ((iconAtlas != null) ? iconAtlas.GetSprite(spriteName) : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		val = PrefabManager.Cache.GetPrefab<Sprite>(spriteName);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		Logger.LogWarning("Sprite " + spriteName + " not found.");
		return null;
	}

	/// <summary>
	///     Creates and displays a Valheim style ColorPicker
	/// </summary>
	/// <param name="anchorMin">Min anchor on first instantiation</param>
	/// <param name="anchorMax">Max anchor on first instantiation</param>
	/// <param name="position">Position on first instantiation</param>
	/// <param name="original">Color before editing</param>
	/// <param name="message">Display message</param>
	/// <param name="onColorChanged">Event that gets called when the color gets modified</param>
	/// <param name="onColorSelected">Event that gets called when one of the buttons done or cancel get pressed</param>
	/// <param name="useAlpha">When set to false the colors used don't have an alpha channel</param>
	/// <returns></returns>
	public void CreateColorPicker(Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Color original, string message, ColorPicker.ColorEvent onColorChanged, ColorPicker.ColorEvent onColorSelected, bool useAlpha = false)
	{
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)CustomGUIFront == (Object)null)
		{
			Logger.LogError("GUIManager CustomGUIFront is null");
			return;
		}
		GameObject prefab = PrefabManager.Instance.GetPrefab("ColorPicker");
		if ((Object)(object)prefab == (Object)null)
		{
			Logger.LogError("ColorPicker is null");
		}
		if ((Object)(object)CustomGUIFront.transform.Find("ColorPicker") == (Object)null)
		{
			GameObject val = Object.Instantiate<GameObject>(prefab, CustomGUIFront.transform, false);
			((Object)val).name = "ColorPicker";
			val.GetComponent<Image>().pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
			RectTransform component = val.GetComponent<RectTransform>();
			component.anchoredPosition = position;
			component.anchorMin = anchorMin;
			component.anchorMax = anchorMax;
		}
		CustomGUIFront.transform.Find("ColorPicker").SetAsLastSibling();
		ColorPicker.Create(original, message, onColorChanged, onColorSelected, useAlpha);
	}

	/// <summary>
	///     Creates and displays a Valheim style GradientPicker
	/// </summary>
	/// <param name="anchorMin">Min anchor on first instantiation</param>
	/// <param name="anchorMax">Max anchor on first instantiation</param>
	/// <param name="position">Position on first instantiation</param>
	/// <param name="original">Color before editing</param>
	/// <param name="message">Display message</param>
	/// <param name="onGradientChanged">Event that gets called when the gradient gets modified</param>
	/// <param name="onGradientSelected">Event that gets called when one of the buttons done or cancel gets pressed</param>
	/// <returns></returns>
	public void CreateGradientPicker(Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Gradient original, string message, GradientPicker.GradientEvent onGradientChanged, GradientPicker.GradientEvent onGradientSelected)
	{
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)CustomGUIFront == (Object)null)
		{
			Logger.LogError("GUIManager CustomGUIFront is null");
			return;
		}
		GameObject prefab = PrefabManager.Instance.GetPrefab("GradientPicker");
		if ((Object)(object)prefab == (Object)null)
		{
			Logger.LogError("GradientPicker is null");
		}
		GameObject prefab2 = PrefabManager.Instance.GetPrefab("ColorPicker");
		if ((Object)(object)prefab2 == (Object)null)
		{
			Logger.LogError("ColorPicker is null");
		}
		if ((Object)(object)CustomGUIFront.transform.Find("GradientPicker") == (Object)null)
		{
			GameObject val = Object.Instantiate<GameObject>(prefab, CustomGUIFront.transform, false);
			((Object)val).name = "GradientPicker";
			val.GetComponent<Image>().pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
			RectTransform component = val.GetComponent<RectTransform>();
			component.anchoredPosition = position;
			component.anchorMin = anchorMin;
			component.anchorMax = anchorMax;
		}
		CustomGUIFront.transform.Find("GradientPicker").SetAsLastSibling();
		if ((Object)(object)CustomGUIFront.transform.Find("ColorPicker") == (Object)null)
		{
			GameObject val2 = Object.Instantiate<GameObject>(prefab2, CustomGUIFront.transform, false);
			((Object)val2).name = "ColorPicker";
			val2.GetComponent<Image>().pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		CustomGUIFront.transform.Find("ColorPicker").SetAsLastSibling();
		GradientPicker.Create(original, message, onGradientChanged, onGradientSelected);
	}

	/// <summary>
	///     Creates a Valheim style woodpanel which is draggable per default
	/// </summary>
	/// <param name="parent">Parent <see cref="T:UnityEngine.Transform" /></param>
	/// <param name="anchorMin">Minimal anchor</param>
	/// <param name="anchorMax">Maximal anchor</param>
	/// <param name="position">Anchored position</param>
	/// <param name="width">Optional width</param>
	/// <param name="height">Optional height</param>
	/// <returns>A <see cref="T:UnityEngine.GameObject" /> as a Valheim style woodpanel</returns>
	public GameObject CreateWoodpanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, float width = 0f, float height = 0f)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		return CreateWoodpanel(parent, anchorMin, anchorMax, position, width, height, draggable: true);
	}

	/// <summary>
	///     Creates a Valheim style woodpanel, can optionally be draggable
	/// </summary>
	/// <param name="parent">Parent <see cref="T:UnityEngine.Transform" /></param>
	/// <param name="anchorMin">Minimal anchor</param>
	/// <param name="anchorMax">Maximal anchor</param>
	/// <param name="position">Anchored position</param>
	/// <param name="width">Optional width</param>
	/// <param name="height">Optional height</param>
	/// <param name="draggable">Optional flag if the panel should be draggable (default true)</param>
	/// <returns>A <see cref="T:UnityEngine.GameObject" /> as a Valheim style woodpanel</returns>
	public GameObject CreateWoodpanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, float width = 0f, float height = 0f, bool draggable = true)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Expected O, but got Unknown
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = DefaultControls.CreatePanel(ValheimControlResources);
		val.transform.SetParent(parent, false);
		ApplyWoodpanelStyle(val.transform);
		val.GetComponent<Image>().pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		RectTransform val2 = (RectTransform)val.transform;
		val2.anchoredPosition = position;
		val2.anchorMin = anchorMin;
		val2.anchorMax = anchorMax;
		if (width > 0f)
		{
			val2.SetSizeWithCurrentAnchors((Axis)0, width);
		}
		if (height > 0f)
		{
			val2.SetSizeWithCurrentAnchors((Axis)1, height);
		}
		if (draggable)
		{
			val.AddComponent<DragWindowCntrl>();
		}
		return val;
	}

	/// <summary>
	///     Create a complete scroll view
	/// </summary>
	/// <param name="parent">parent transform</param>
	/// <param name="showHorizontalScrollbar">show horizontal scrollbar</param>
	/// <param name="showVerticalScrollbar">show vertical scrollbar</param>
	/// <param name="handleSize">size of the handle</param>
	/// <param name="handleDistanceToBorder"></param>
	/// <param name="handleColors">Colorblock for the handle</param>
	/// <param name="slidingAreaBackgroundColor">Background color for the sliding area</param>
	/// <param name="width">rect width</param>
	/// <param name="height">rect height</param>
	/// <returns></returns>
	public GameObject CreateScrollView(Transform parent, bool showHorizontalScrollbar, bool showVerticalScrollbar, float handleSize, float handleDistanceToBorder, ColorBlock handleColors, Color slidingAreaBackgroundColor, float width, float height)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected O, but got Unknown
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Expected O, but got Unknown
		//IL_01ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_0254: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Expected O, but got Unknown
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0284: Unknown result type (might be due to invalid IL or missing references)
		//IL_029e: Unknown result type (might be due to invalid IL or missing references)
		//IL_02de: Unknown result type (might be due to invalid IL or missing references)
		//IL_0350: Unknown result type (might be due to invalid IL or missing references)
		//IL_0357: Expected O, but got Unknown
		//IL_038d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_041e: Unknown result type (might be due to invalid IL or missing references)
		//IL_042c: Unknown result type (might be due to invalid IL or missing references)
		//IL_044b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0452: Expected O, but got Unknown
		//IL_0463: Unknown result type (might be due to invalid IL or missing references)
		//IL_047e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0499: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0521: Unknown result type (might be due to invalid IL or missing references)
		//IL_0528: Expected O, but got Unknown
		//IL_053d: Unknown result type (might be due to invalid IL or missing references)
		//IL_096e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0974: Expected O, but got Unknown
		//IL_0994: Unknown result type (might be due to invalid IL or missing references)
		//IL_09ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_09c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0632: Unknown result type (might be due to invalid IL or missing references)
		//IL_0639: Expected O, but got Unknown
		//IL_066f: Unknown result type (might be due to invalid IL or missing references)
		//IL_068a: Unknown result type (might be due to invalid IL or missing references)
		//IL_06a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_070d: Unknown result type (might be due to invalid IL or missing references)
		//IL_072c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0733: Expected O, but got Unknown
		//IL_0744: Unknown result type (might be due to invalid IL or missing references)
		//IL_075f: Unknown result type (might be due to invalid IL or missing references)
		//IL_077a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0795: Unknown result type (might be due to invalid IL or missing references)
		//IL_0802: Unknown result type (might be due to invalid IL or missing references)
		//IL_0809: Expected O, but got Unknown
		//IL_081a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0835: Unknown result type (might be due to invalid IL or missing references)
		//IL_0853: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = new GameObject("Canvas", new Type[3]
		{
			typeof(RectTransform),
			typeof(CanvasGroup),
			typeof(GraphicRaycaster)
		});
		val.GetComponent<Canvas>().sortingOrder = 0;
		val.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
		val.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
		val.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
		((Transform)val.GetComponent<RectTransform>()).localPosition = new Vector3(0f, 0f, 0f);
		((Transform)val.GetComponent<RectTransform>()).position = new Vector3(0f, 0f, 0f);
		val.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 0f);
		val.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width);
		val.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height);
		val.GetComponent<CanvasGroup>().interactable = true;
		val.GetComponent<CanvasGroup>().ignoreParentGroups = true;
		val.GetComponent<CanvasGroup>().blocksRaycasts = true;
		val.GetComponent<CanvasGroup>().alpha = 1f;
		val.transform.SetParent(parent, false);
		GameObject val2 = GameObjectGUIExtension.SetUpperRight(new GameObject("Scroll View", new Type[3]
		{
			typeof(Image),
			typeof(ScrollRect),
			typeof(Mask)
		}));
		val2.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width);
		val2.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height);
		((Graphic)val2.GetComponent<Image>()).color = new Color(0f, 0f, 0f, 1f);
		val2.GetComponent<ScrollRect>().horizontal = showHorizontalScrollbar;
		val2.GetComponent<ScrollRect>().vertical = showVerticalScrollbar;
		val2.GetComponent<ScrollRect>().horizontalScrollbarVisibility = (ScrollbarVisibility)1;
		val2.GetComponent<ScrollRect>().verticalScrollbarVisibility = (ScrollbarVisibility)2;
		val2.GetComponent<ScrollRect>().scrollSensitivity = 35f;
		val2.GetComponent<Mask>().showMaskGraphic = false;
		val2.transform.SetParent(val.transform, false);
		GameObject val3 = new GameObject("Viewport", new Type[3]
		{
			typeof(RectTransform),
			typeof(CanvasRenderer),
			typeof(Image)
		});
		val3.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
		val3.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
		val3.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
		val3.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width);
		val3.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height);
		((Graphic)val3.GetComponent<Image>()).color = new Color(0f, 0f, 0f, 0f);
		val3.transform.SetParent(val2.transform, false);
		val2.GetComponent<ScrollRect>().viewport = val3.GetComponent<RectTransform>();
		if (showHorizontalScrollbar)
		{
			GameObject val4 = new GameObject("Scrollbar horizontal", new Type[4]
			{
				typeof(RectTransform),
				typeof(CanvasRenderer),
				typeof(Image),
				typeof(Scrollbar)
			});
			val4.transform.SetParent(val2.transform, false);
			val2.GetComponent<ScrollRect>().horizontalScrollbar = val4.GetComponent<Scrollbar>();
			val4.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1f);
			val4.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);
			val4.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
			val4.GetComponent<RectTransform>().anchoredPosition = new Vector2((0f - handleSize) / 2f, 0f - height + handleSize);
			val4.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width - 2f * handleDistanceToBorder - handleSize);
			val4.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, handleSize);
			((Graphic)val4.GetComponent<Image>()).color = slidingAreaBackgroundColor;
			((Selectable)val4.GetComponent<Scrollbar>()).colors = handleColors;
			GameObject val5 = new GameObject("Sliding Area", new Type[1] { typeof(RectTransform) });
			val5.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
			val5.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
			val5.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
			val5.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 0f);
			val5.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width - 2f * handleDistanceToBorder - handleSize);
			val5.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, handleSize);
			val5.transform.SetParent(val4.transform, false);
			GameObject val6 = new GameObject("Handle", new Type[2]
			{
				typeof(RectTransform),
				typeof(Image)
			});
			val6.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, (0f - handleSize) / 2f);
			val6.transform.SetParent(val5.transform, false);
			val6.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, handleSize / 2f);
			val6.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, handleSize / 2f);
			val6.GetComponent<Image>().sprite = GetSprite("UISprite");
			val6.GetComponent<Image>().type = (Type)1;
			val4.GetComponent<Scrollbar>().size = 0.4f;
			val4.GetComponent<Scrollbar>().handleRect = val6.GetComponent<RectTransform>();
			((Selectable)val4.GetComponent<Scrollbar>()).targetGraphic = (Graphic)(object)val6.GetComponent<Image>();
			val4.GetComponent<Scrollbar>().direction = (Direction)0;
		}
		if (showVerticalScrollbar)
		{
			GameObject val7 = new GameObject("Scrollbar Vertical", new Type[4]
			{
				typeof(RectTransform),
				typeof(CanvasRenderer),
				typeof(Image),
				typeof(Scrollbar)
			});
			val7.transform.SetParent(val2.transform, false);
			val2.GetComponent<ScrollRect>().verticalScrollbar = val7.GetComponent<Scrollbar>();
			val7.GetComponent<RectTransform>().anchorMin = new Vector2(1f, 0.5f);
			val7.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 0.5f);
			val7.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
			val7.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, (0f - handleSize) / 2f);
			val7.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, handleSize);
			val7.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height - 2f * handleDistanceToBorder - handleSize);
			((Graphic)val7.GetComponent<Image>()).color = slidingAreaBackgroundColor;
			((Selectable)val7.GetComponent<Scrollbar>()).colors = handleColors;
			GameObject val8 = new GameObject("Sliding Area", new Type[1] { typeof(RectTransform) });
			val8.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
			val8.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
			val8.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
			val8.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 0f);
			val8.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, handleSize);
			val8.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height - 2f * handleDistanceToBorder - handleSize);
			val8.transform.SetParent(val7.transform, false);
			GameObject val9 = new GameObject("Handle", new Type[2]
			{
				typeof(RectTransform),
				typeof(Image)
			});
			val9.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
			val9.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
			val9.GetComponent<RectTransform>().anchoredPosition = new Vector2(handleSize / 2f, 0f);
			val9.transform.SetParent(val8.transform, false);
			val9.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, handleSize / 2f);
			val9.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, handleSize / 2f);
			val9.GetComponent<Image>().sprite = GetSprite("UISprite");
			val9.GetComponent<Image>().type = (Type)1;
			val7.GetComponent<Scrollbar>().size = 0.4f;
			val7.GetComponent<Scrollbar>().handleRect = val9.GetComponent<RectTransform>();
			((Selectable)val7.GetComponent<Scrollbar>()).targetGraphic = (Graphic)(object)val9.GetComponent<Image>();
			val7.GetComponent<Scrollbar>().size = handleSize;
			val7.GetComponent<Scrollbar>().direction = (Direction)2;
			val7.GetComponent<Scrollbar>().SetValueWithoutNotify(1f);
		}
		GameObject val10 = new GameObject("Content", new Type[5]
		{
			typeof(RectTransform),
			typeof(VerticalLayoutGroup),
			typeof(Canvas),
			typeof(GraphicRaycaster),
			typeof(ContentSizeFitter)
		});
		val10.GetComponent<Canvas>().planeDistance = 5.2f;
		val10.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
		val10.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
		val10.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
		val10.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width - 2f * handleDistanceToBorder - handleSize);
		val10.GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height - 2f * handleDistanceToBorder - handleSize);
		((LayoutGroup)val10.GetComponent<VerticalLayoutGroup>()).childAlignment = (TextAnchor)0;
		((HorizontalOrVerticalLayoutGroup)val10.GetComponent<VerticalLayoutGroup>()).childForceExpandWidth = true;
		((HorizontalOrVerticalLayoutGroup)val10.GetComponent<VerticalLayoutGroup>()).childForceExpandHeight = false;
		((HorizontalOrVerticalLayoutGroup)val10.GetComponent<VerticalLayoutGroup>()).childControlHeight = true;
		((HorizontalOrVerticalLayoutGroup)val10.GetComponent<VerticalLayoutGroup>()).childControlWidth = showHorizontalScrollbar;
		val10.transform.SetParent(val3.transform, false);
		val10.GetComponent<ContentSizeFitter>().verticalFit = (FitMode)2;
		val2.GetComponent<ScrollRect>().content = val10.GetComponent<RectTransform>();
		return val;
	}

	/// <summary>
	///     Create a <see cref="T:UnityEngine.GameObject" /> with a Text (and optional Outline and ContentSizeFitter) component
	/// </summary>
	/// <param name="text">Text to show</param>
	/// <param name="parent">Parent transform</param>
	/// <param name="anchorMin">Anchor min</param>
	/// <param name="anchorMax">Anchor max</param>
	/// <param name="position">Anchored position</param>
	/// <param name="font">Font</param>
	/// <param name="fontSize">Font size</param>
	/// <param name="color">Font color</param>
	/// <param name="outline">Add outline component</param>
	/// <param name="outlineColor">Outline color</param>
	/// <param name="width">Width</param>
	/// <param name="height">Height</param>
	/// <param name="addContentSizeFitter">Add ContentSizeFitter</param>
	/// <returns>A text <see cref="T:UnityEngine.GameObject" /></returns>
	public GameObject CreateText(string text, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Font font, int fontSize, Color color, bool outline, Color outlineColor, float width, float height, bool addContentSizeFitter)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = new GameObject("Text", new Type[2]
		{
			typeof(RectTransform),
			typeof(Text)
		});
		RectTransform component = val.GetComponent<RectTransform>();
		component.anchorMin = anchorMin;
		component.anchorMax = anchorMax;
		component.anchoredPosition = position;
		if (!addContentSizeFitter)
		{
			component.SetSizeWithCurrentAnchors((Axis)0, width);
			component.SetSizeWithCurrentAnchors((Axis)1, height);
		}
		else
		{
			val.AddComponent<ContentSizeFitter>().verticalFit = (FitMode)2;
		}
		Text component2 = val.GetComponent<Text>();
		component2.text = text;
		ApplyTextStyle(component2, font, color, fontSize, outline);
		Outline val2 = default(Outline);
		if (val.TryGetComponent<Outline>(ref val2))
		{
			((Shadow)val2).effectColor = outlineColor;
		}
		val.transform.SetParent(parent, false);
		return val;
	}

	/// <summary>
	///     Create a new button (Valheim style).
	/// </summary>
	/// <param name="text">Text to display on the button</param>
	/// <param name="parent">Parent transform</param>
	/// <param name="anchorMin">Min anchor</param>
	/// <param name="anchorMax">Max anchor</param>
	/// <param name="position">Position</param>
	/// <param name="width">Set width if &gt; 0</param>
	/// <param name="height">Set height if &gt; 0</param>
	/// <returns>Button GameObject in Valheim style</returns>
	public GameObject CreateButton(string text, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, float width = 0f, float height = 0f)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = DefaultControls.CreateButton(ValheimControlResources);
		val.transform.SetParent(parent, false);
		ApplyButtonStyle(val.GetComponent<Button>());
		Text componentInChildren = val.GetComponentInChildren<Text>();
		componentInChildren.text = text;
		Transform transform = val.transform;
		RectTransform val2 = (RectTransform)(object)((transform is RectTransform) ? transform : null);
		val2.anchoredPosition = position;
		val2.anchorMin = anchorMin;
		val2.anchorMax = anchorMax;
		if (width > 0f)
		{
			val2.SetSizeWithCurrentAnchors((Axis)0, width);
			((Component)componentInChildren).GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)0, width);
		}
		if (height > 0f)
		{
			val2.SetSizeWithCurrentAnchors((Axis)1, height);
			((Component)componentInChildren).GetComponent<RectTransform>().SetSizeWithCurrentAnchors((Axis)1, height);
		}
		return val;
	}

	/// <summary>
	///     Create a new InputField (Valheim style).
	/// </summary>
	/// <param name="parent">Parent transform</param>
	/// <param name="anchorMin">Min anchor</param>
	/// <param name="anchorMax">Max anchor</param>
	/// <param name="position">Position</param>
	/// <param name="contentType">Content type for the input field</param>
	/// <param name="placeholderText">Text to display as a placeholder (can be null)</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	/// <param name="width">Set width if &gt; 0</param>
	/// <param name="height">Set height if &gt; 0</param>
	/// <returns>Input field GameObject in Valheim style</returns>
	public GameObject CreateInputField(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, ContentType contentType = (ContentType)0, string placeholderText = null, int fontSize = 16, float width = 0f, float height = 0f)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = DefaultControls.CreateInputField(ValheimControlResources);
		val.transform.SetParent(parent, false);
		InputField component = val.GetComponent<InputField>();
		ApplyInputFieldStyle(component, fontSize);
		component.contentType = contentType;
		if (!string.IsNullOrEmpty(placeholderText))
		{
			Graphic placeholder = component.placeholder;
			Text val2 = (Text)(object)((placeholder is Text) ? placeholder : null);
			if (val2 != null)
			{
				val2.text = placeholderText;
			}
		}
		Transform transform = val.transform;
		RectTransform val3 = (RectTransform)(object)((transform is RectTransform) ? transform : null);
		val3.anchoredPosition = position;
		val3.anchorMin = anchorMin;
		val3.anchorMax = anchorMax;
		if (width > 0f)
		{
			val.SetWidth(width);
			((Component)component.placeholder).gameObject.SetWidth(width - 20f);
			((Component)component.textComponent).gameObject.SetWidth(width - 20f);
		}
		if (height > 0f)
		{
			val.SetHeight(height);
			((Component)component.placeholder).gameObject.SetHeight(height - 10f);
			((Component)component.textComponent).gameObject.SetHeight(height - 10f);
		}
		return val;
	}

	/// <summary>
	///     Create toggle field
	/// </summary>
	/// <param name="parent">Parent transform</param>
	/// <param name="width">Set width</param>
	/// <param name="height">Set height</param>
	/// <returns></returns>
	public GameObject CreateToggle(Transform parent, float width, float height)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = DefaultControls.CreateToggle(ValheimControlResources);
		val.transform.SetParent(parent);
		Toggle component = val.GetComponent<Toggle>();
		ApplyToogleStyle(component);
		val.SetSize(width, height);
		((Component)val.transform.Find("Background")).gameObject.SetSize(width, height);
		((Component)val.transform.Find("Background/Checkmark")).gameObject.SetSize(width, height);
		return val;
	}

	/// <summary>
	///     Create dropdown field
	/// </summary>
	/// <param name="parent">Parent transform</param>
	/// <param name="anchorMin">Min anchor</param>
	/// <param name="anchorMax">Max anchor</param>
	/// <param name="position">Position</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	/// <param name="width">Set width if &gt; 0</param>
	/// <param name="height">Set height if &gt; 0</param>
	/// <returns></returns>
	public GameObject CreateDropDown(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, int fontSize = 16, float width = 0f, float height = 0f)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = DefaultControls.CreateDropdown(ValheimControlResources);
		val.transform.SetParent(parent, false);
		Dropdown component = val.GetComponent<Dropdown>();
		component.ClearOptions();
		ApplyDropdownStyle(component, fontSize);
		Transform transform = val.transform;
		RectTransform val2 = (RectTransform)(object)((transform is RectTransform) ? transform : null);
		val2.anchoredPosition = position;
		val2.anchorMin = anchorMin;
		val2.anchorMax = anchorMax;
		if (width > 0f)
		{
			float width2 = width - 50f;
			val.SetWidth(width);
			((Component)component.captionText).gameObject.SetMiddleLeft();
			((Component)component.captionText).gameObject.SetWidth(width2);
			((Component)component.captionText).GetComponent<RectTransform>().anchoredPosition = Vector2.op_Implicit(new Vector3(10f, 0f));
			((Component)component.itemText).gameObject.SetWidth(width2);
		}
		if (height > 0f)
		{
			val.SetHeight(height);
			((Component)component.captionText).gameObject.SetMiddleLeft();
			((Component)component.captionText).gameObject.SetHeight(height);
			((Component)component.captionText).GetComponent<RectTransform>().anchoredPosition = Vector2.op_Implicit(new Vector3(10f, 0f));
			((Component)component.itemText).gameObject.SetHeight(height);
		}
		return val;
	}

	/// <summary>
	///     Create key binding field
	/// </summary>
	/// <param name="text"></param>
	/// <param name="parent"></param>
	/// <param name="width"></param>
	/// <param name="height"></param>
	/// <returns></returns>
	public GameObject CreateKeyBindField(string text, Transform parent, float width, float height)
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected O, but got Unknown
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Expected O, but got Unknown
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0187: Expected O, but got Unknown
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cb: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = GameObjectGUIExtension.SetUpperLeft(new GameObject("KeyBinding", new Type[2]
		{
			typeof(RectTransform),
			typeof(LayoutElement)
		})).SetSize(width, height);
		val.GetComponent<LayoutElement>().preferredWidth = width;
		GameObject val2 = CreateText(text, val.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), AveriaSerifBold, 16, ValheimOrange, outline: true, Color.black, width - 150f, 0f, addContentSizeFitter: false);
		val2.GetComponent<Text>().verticalOverflow = (VerticalWrapMode)1;
		val2.SetUpperLeft().SetToTextHeight();
		val.SetHeight(val2.GetTextHeight());
		val.transform.SetParent(parent, false);
		GameObject val3 = GameObjectGUIExtension.SetUpperRight(new GameObject("Button", new Type[3]
		{
			typeof(RectTransform),
			typeof(Image),
			typeof(Button)
		})).SetSize(140f, val2.GetTextHeight());
		val3.transform.SetParent(val.transform, false);
		((Selectable)val3.GetComponent<Button>()).image = val3.GetComponent<Image>();
		val3.GetComponent<Image>().sprite = GetSprite("text_field");
		GameObject val4 = GameObjectGUIExtension.SetMiddleCenter(new GameObject("Text", new Type[2]
		{
			typeof(RectTransform),
			typeof(Text)
		}));
		Text component = val4.GetComponent<Text>();
		ApplyTextStyle(component, component.fontSize);
		component.text = "";
		GameObject go = val4.SetHeight(val4.GetTextHeight() + height - 2f);
		Rect rect = val3.GetComponent<RectTransform>().rect;
		go.SetWidth(((Rect)(ref rect)).width);
		val4.SetMiddleLeft().GetComponent<Text>().alignment = (TextAnchor)4;
		val3.SetHeight(val4.GetTextHeight() + height);
		val4.transform.SetParent(val3.transform, false);
		return val;
	}

	/// <summary>
	///     Apply Valheim style to a woodpanel.
	/// </summary>
	/// <param name="woodpanel"></param>
	public void ApplyWoodpanelStyle(Transform woodpanel)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		((Component)woodpanel).GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
		((Component)woodpanel).GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
		((Component)woodpanel).GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 0f);
		((Component)woodpanel).GetComponent<Image>().sprite = GetSprite("woodpanel_trophys");
		((Component)woodpanel).GetComponent<Image>().type = (Type)1;
		((Component)woodpanel).GetComponent<Image>().pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		((Graphic)((Component)woodpanel).GetComponent<Image>()).material = PrefabManager.Cache.GetPrefab<Material>("litpanel");
		((Graphic)((Component)woodpanel).GetComponent<Image>()).color = Color.white;
		((Component)woodpanel).gameObject.layer = 5;
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Text" /> Component
	/// </summary>
	/// <param name="text">Target component</param>
	/// <param name="font">Own font or <code>GUIManager.Instance.AveriaSerifBold</code>/<code>GUIManager.Instance.AveriaSerif</code></param>
	/// <param name="color">Custom color or <code>GUIManager.Instance.ValheimOrange</code></param>
	/// <param name="createOutline">creates an <see cref="T:UnityEngine.UI.Outline" /> component when true</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	public void ApplyTextStyle(Text text, Font font, Color color, int fontSize = 16, bool createOutline = true)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		text.font = font;
		text.fontSize = fontSize;
		((Graphic)text).color = color;
		if (createOutline)
		{
			Outline orAddComponent = ((Component)text).gameObject.GetOrAddComponent<Outline>();
			((Shadow)orAddComponent).effectColor = Color.black;
		}
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Text" /> Component.
	///     Uses <code>GUIManager.Instance.AveriaSerifBold</code> by default
	/// </summary>
	/// <param name="text">Target component</param>
	/// <param name="color">Custom color or <code>GUIManager.Instance.ValheimOrange</code></param>
	/// <param name="createOutline">creates an <see cref="T:UnityEngine.UI.Outline" /> component when true</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	public void ApplyTextStyle(Text text, Color color, int fontSize = 16, bool createOutline = true)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		ApplyTextStyle(text, AveriaSerifBold, color, fontSize, createOutline);
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Text" /> Component.
	///     Uses <code>GUIManager.Instance.AveriaSerifBold</code>, <code>Color.white</code> and creates an outline by default
	/// </summary>
	/// <param name="text">Target component</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	public void ApplyTextStyle(Text text, int fontSize = 16)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		ApplyTextStyle(text, AveriaSerifBold, Color.white, fontSize);
	}

	/// <summary>
	///     Apply valheim style to a <see cref="T:UnityEngine.UI.Button" /> Component
	/// </summary>
	/// <param name="button">Component to apply the style to</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	public void ApplyButtonStyle(Button button, int fontSize = 16)
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		GameObject gameObject = ((Component)button).gameObject;
		Image component = gameObject.GetComponent<Image>();
		if (Object.op_Implicit((Object)(object)component))
		{
			component.sprite = GetSprite("button");
			component.type = (Type)1;
			component.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
			((Selectable)button).image = component;
		}
		ButtonSfx val = default(ButtonSfx);
		if (!gameObject.TryGetComponent<ButtonSfx>(ref val))
		{
			val = gameObject.AddComponent<ButtonSfx>();
		}
		val.m_sfxPrefab = PrefabManager.Cache.GetPrefab<GameObject>("sfx_gui_button");
		val.m_selectSfxPrefab = PrefabManager.Cache.GetPrefab<GameObject>("sfx_gui_select");
		((Selectable)gameObject.GetComponent<Button>()).colors = ValheimButtonColorBlock;
		Text componentInChildren = gameObject.GetComponentInChildren<Text>(true);
		if (Object.op_Implicit((Object)(object)componentInChildren))
		{
			ApplyTextStyle(componentInChildren, ValheimOrange, fontSize);
			componentInChildren.alignment = (TextAnchor)4;
		}
	}

	/// <summary>
	///     Apply Valheim style to an <see cref="T:UnityEngine.UI.InputField" /> Component.
	/// </summary>
	/// <param name="field">Component to apply the style to</param>
	[Obsolete("Only here for backward compat")]
	public void ApplyInputFieldStyle(InputField field)
	{
		ApplyInputFieldStyle(field, 16);
	}

	/// <summary>
	///     Apply Valheim style to an <see cref="T:UnityEngine.UI.InputField" /> Component.
	/// </summary>
	/// <param name="field">Component to apply the style to</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	public void ApplyInputFieldStyle(InputField field, int fontSize = 16)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		Graphic targetGraphic = ((Selectable)field).targetGraphic;
		Image val = (Image)(object)((targetGraphic is Image) ? targetGraphic : null);
		if (val != null)
		{
			((Graphic)val).color = Color.white;
			val.sprite = GetSprite("text_field");
			val.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		Graphic placeholder = field.placeholder;
		Text val2 = (Text)(object)((placeholder is Text) ? placeholder : null);
		if (val2 != null)
		{
			val2.font = AveriaSerifBold;
			((Graphic)val2).color = Color.grey;
			val2.fontSize = fontSize;
		}
		if (Object.op_Implicit((Object)(object)field.textComponent))
		{
			ApplyTextStyle(field.textComponent, fontSize);
		}
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Toggle" /> component.
	/// </summary>
	/// <param name="toggle">Component to apply the style to</param>
	public void ApplyToogleStyle(Toggle toggle)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		toggle.toggleTransition = (ToggleTransition)1;
		((Selectable)toggle).colors = ValheimToggleColorBlock;
		Graphic targetGraphic = ((Selectable)toggle).targetGraphic;
		Image val = (Image)(object)((targetGraphic is Image) ? targetGraphic : null);
		if (val != null)
		{
			val.sprite = GetSprite("checkbox");
			val.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		Graphic graphic = toggle.graphic;
		Image val2 = (Image)(object)((graphic is Image) ? graphic : null);
		if (val2 != null)
		{
			((Graphic)val2).color = new Color(1f, 0.678f, 0.103f, 1f);
			val2.sprite = GetSprite("checkbox_marker");
			val2.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
			((MaskableGraphic)val2).maskable = true;
		}
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Dropdown" /> component.
	/// </summary>
	/// <param name="dropdown">Component to apply the style to</param>
	/// <param name="fontSize">Optional font size, defaults to 16</param>
	public void ApplyDropdownStyle(Dropdown dropdown, int fontSize = 16)
	{
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		((Component)dropdown).gameObject.layer = 5;
		if ((Object)(object)dropdown.template != (Object)null)
		{
			((Component)dropdown.template).gameObject.layer = 5;
		}
		if (Object.op_Implicit((Object)(object)dropdown.captionText))
		{
			ApplyTextStyle(dropdown.captionText, fontSize);
			dropdown.captionText.verticalOverflow = (VerticalWrapMode)1;
		}
		if (Object.op_Implicit((Object)(object)dropdown.itemText))
		{
			ApplyTextStyle(dropdown.itemText, fontSize);
			dropdown.captionText.verticalOverflow = (VerticalWrapMode)1;
		}
		Image val = default(Image);
		if (((Component)dropdown).TryGetComponent<Image>(ref val))
		{
			val.sprite = GetSprite("text_field");
			val.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		GameObject gameObject = ((Component)((Component)dropdown).transform.Find("Arrow")).gameObject;
		gameObject.transform.localRotation = Quaternion.Euler(new Vector3(0f, 0f, 180f));
		Image val2 = default(Image);
		if (gameObject.TryGetComponent<Image>(ref val2))
		{
			gameObject.SetSize(25f, 25f);
			val2.sprite = GetSprite("map_marker");
			((Graphic)val2).color = Color.white;
			val2.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		ScrollRect scrollRect = default(ScrollRect);
		if (Object.op_Implicit((Object)(object)dropdown.template) && ((Component)dropdown.template).TryGetComponent<ScrollRect>(ref scrollRect))
		{
			ApplyScrollRectStyle(scrollRect);
		}
		Image val3 = default(Image);
		if (Object.op_Implicit((Object)(object)dropdown.template) && ((Component)dropdown.template).TryGetComponent<Image>(ref val3))
		{
			val3.sprite = GetSprite("button_small");
			((Graphic)val3).color = Color.white;
			val3.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		GameObject gameObject2 = ((Component)((Transform)dropdown.template).Find("Viewport/Content/Item")).gameObject;
		Toggle val4 = default(Toggle);
		if (Object.op_Implicit((Object)(object)gameObject2) && gameObject2.TryGetComponent<Toggle>(ref val4))
		{
			val4.toggleTransition = (ToggleTransition)0;
			((Selectable)val4).colors = ValheimToggleColorBlock;
			Toggle obj = val4;
			SpriteState spriteState = default(SpriteState);
			((SpriteState)(ref spriteState)).highlightedSprite = GetSprite("button_highlight");
			((Selectable)obj).spriteState = spriteState;
			Graphic targetGraphic = ((Selectable)val4).targetGraphic;
			Image val5 = (Image)(object)((targetGraphic is Image) ? targetGraphic : null);
			if (val5 != null)
			{
				((Behaviour)val5).enabled = false;
			}
			Graphic graphic = val4.graphic;
			Image val6 = (Image)(object)((graphic is Image) ? graphic : null);
			if (val6 != null)
			{
				val6.sprite = GetSprite("checkbox_marker");
				((Graphic)val6).color = Color.white;
				val6.type = (Type)0;
				((MaskableGraphic)val6).maskable = true;
				val6.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
				((Shadow)((Component)val6).gameObject.GetOrAddComponent<Outline>()).effectColor = Color.black;
			}
		}
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.ScrollRect" /> component.
	/// </summary>
	/// <param name="scrollRect">Component to apply the style to</param>
	public void ApplyScrollRectStyle(ScrollRect scrollRect)
	{
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		scrollRect.scrollSensitivity = 40f;
		if (Object.op_Implicit((Object)(object)scrollRect.horizontalScrollbar))
		{
			ApplyScrollbarStyle(scrollRect.horizontalScrollbar);
		}
		if (Object.op_Implicit((Object)(object)scrollRect.verticalScrollbar))
		{
			ApplyScrollbarStyle(scrollRect.verticalScrollbar);
		}
		Image val = default(Image);
		if (((Component)scrollRect).TryGetComponent<Image>(ref val))
		{
			((Graphic)val).color = new Color(0f, 0f, 0f, 0.564f);
			val.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Scrollbar" /> component.
	/// </summary>
	/// <param name="scrollbar">Component to apply the style to</param>
	public void ApplyScrollbarStyle(Scrollbar scrollbar)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Invalid comparison between Unknown and I4
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Invalid comparison between Unknown and I4
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Invalid comparison between Unknown and I4
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		((Selectable)scrollbar).transition = (Transition)1;
		((Selectable)scrollbar).colors = ValheimScrollbarHandleColorBlock;
		RectTransform val = (RectTransform)((Component)scrollbar).transform;
		if ((int)scrollbar.direction == 0 || (int)scrollbar.direction == 1)
		{
			val.sizeDelta = new Vector2(val.sizeDelta.x, 10f);
		}
		if ((int)scrollbar.direction == 2 || (int)scrollbar.direction == 3)
		{
			val.sizeDelta = new Vector2(10f, val.sizeDelta.y);
		}
		Graphic targetGraphic = ((Selectable)scrollbar).targetGraphic;
		Image val2 = (Image)(object)((targetGraphic is Image) ? targetGraphic : null);
		if (val2 != null)
		{
			val2.sprite = GetSprite("UISprite");
			val2.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
		Image val3 = default(Image);
		if (((Component)scrollbar).TryGetComponent<Image>(ref val3))
		{
			val3.sprite = GetSprite("Background");
			((Graphic)val3).color = Color.black;
			((Graphic)val3).raycastTarget = true;
			val3.pixelsPerUnitMultiplier = (GUIInStart ? 2f : 1f);
		}
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Slider" /> component.
	/// </summary>
	/// <param name="slider"></param>
	public void ApplySliderStyle(Slider slider)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		ApplySliderStyle(slider, new Vector2(40f, 10f));
	}

	/// <summary>
	///     Apply Valheim style to a <see cref="T:UnityEngine.UI.Slider" /> component.
	/// </summary>
	/// <param name="slider"></param>
	/// <param name="handleSize"></param>
	public void ApplySliderStyle(Slider slider, Vector2 handleSize)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Expected O, but got Unknown
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected O, but got Unknown
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		slider.handleRect.sizeDelta = handleSize;
		Image val = default(Image);
		if (Object.op_Implicit((Object)(object)slider.fillRect) && ((Component)slider.fillRect).TryGetComponent<Image>(ref val))
		{
			val.sprite = GetSprite("UISprite");
			((Graphic)val).color = ValheimOrange;
		}
		if (Object.op_Implicit((Object)(object)slider.handleRect) && Object.op_Implicit((Object)(object)((Component)slider.handleRect).transform.parent))
		{
			RectTransform val2 = (RectTransform)((Component)slider.handleRect).transform.parent;
			val2.offsetMin = new Vector2(5f, val2.offsetMin.y);
			val2.offsetMax = new Vector2(-5f, val2.offsetMax.y);
		}
		if (Object.op_Implicit((Object)(object)slider.handleRect) && Object.op_Implicit((Object)(object)((Component)slider.fillRect).transform.parent))
		{
			RectTransform val3 = (RectTransform)((Component)slider.fillRect).transform.parent;
			val3.offsetMin = new Vector2(5f, val3.offsetMin.y);
			val3.offsetMax = new Vector2(-5f, val3.offsetMax.y);
		}
	}
}
