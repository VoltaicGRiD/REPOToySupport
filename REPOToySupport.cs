using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Lovense.UnityKit;
using Lovense.UnityKit.Remote;
using MenuLib;
using MenuLib.MonoBehaviors;
using Photon.Pun;
using REPOToySupport.Patches;
using Sirenix.Utilities;
using Steamworks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace REPOToySupport
{

    [BepInPlugin("VoltaicGRiD.REPOToySupport", "REPOToySupport", "1.0")]
    public class REPOToySupport : BaseUnityPlugin
    {
        internal static REPOToySupport Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger => Instance._logger;
        private ManualLogSource _logger => base.Logger;
        internal Harmony? Harmony { get; set; }

        private List<RemoteAppData> allAppDatas;
        private RemoteAppData thisRemoteAppData;
        private LovenseRemoteToy thisToy;
        private List<LovenseRemoteToy> allToys = new List<LovenseRemoteToy>();
        private List<LovenseRemoteToy> selectedToys = new List<LovenseRemoteToy>();
        private REPOPopupPage toySettingsPage;

        private string lovenseIP = string.Empty;
        private int lovensePort = 30010;

        private PhysGrabObject lastGrab;
        private float lastGrabValue;

        private ConfigEntry<string> vibrationSetting;
        private ConfigEntry<float> vibrationMax;
        private ConfigEntry<float> vibrationTimeout;
        private ConfigEntry<bool> vibrateOnHurt;

        private void Awake()
        {
            Instance = this;

            vibrationSetting = Config.Bind("Toy Support", "Vibration Based On", "value", new ConfigDescription("""
                How should the toy interact with the environment? 
                \"weight\" will use the weight of the grabbed object, scaled to the maximum weight of all objects in the level.
                \"value\" will use the value of the grabbed object, scaled to the maximum value of all valuables in the level.
                \"impact\" will use the impact severity of a collision while holding a valuable: light impacts = 'maxVibration' / 4, medium impacts = 'maxVibration' / 2, heavy impacts = 'maxVibration'.
            """));
                
            vibrationMax = Config.Bind("Toy Support", "Max Vibration", 1f, new ConfigDescription("The maximum vibration strength that can be sent to the toys. This setting does not override the value in your Lovense app, and is instead, a percentage of that maximum.", acceptableValues: new AcceptableValueRange<float>(0f, 1f)));

            vibrationTimeout = Config.Bind("Toy Support", "Vibration Timeout", 100f, new ConfigDescription("The timeout for the vibration command in seconds. This is the time after which the toy will stop vibrating after grabbing an item.", acceptableValues: new AcceptableValueRange<float>(0f, 100f)));

            vibrateOnHurt = Config.Bind("Toy Support", "Vibrate On Hurt", true, new ConfigDescription("Whether to vibrate the toys when the player is hurt. This is useful for adding an extra layer of immersion to the game."));

            // Prevent the plugin from being deleted
            this.gameObject.transform.parent = null;
            this.gameObject.hideFlags = HideFlags.HideAndDontSave;

            LovenseRemote.onGetAppsEvent.AddListener(OnSearchLocalApp);
            LovenseRemote.onGetAppsErrorEvent.AddListener(OnGetAppError);
            LovenseRemote.onGetToysEvent.AddListener(OnGetToys);
            LovenseRemote.onGetToysErrorEvent.AddListener(OnGetToysError);
            LovenseRemote.onControlToysEvent.AddListener(OnControlToysResult);

            MenuAPI.AddElementToSettingsMenu(parent =>
            {
                // Settings page
                toySettingsPage = MenuAPI.CreateREPOPopupPage("Toy Support Settings", REPOPopupPage.PresetSide.Right, shouldCachePage: true);

                toySettingsPage.AddElementToScrollView(scrollView =>
                {
                    var ipInput = MenuAPI.CreateREPOInputField("IP", (ipAddress) => lovenseIP = ipAddress, scrollView, localPosition: Vector2.zero);

                    return ipInput.rectTransform;
                });

                toySettingsPage.AddElementToScrollView(scrollView =>
                {
                    var portInput = MenuAPI.CreateREPOSlider("Port", "The SLL port", (port) => lovensePort = port, scrollView, localPosition: Vector2.zero, min: 0, max: 999999, defaultValue: 30010);

                    return portInput.rectTransform;
                });

                toySettingsPage.AddElementToScrollView(scrollView =>
                {
                    var searchButton = MenuAPI.CreateREPOButton("Search", () => DoSearchToys(lovenseIP, lovensePort), scrollView, localPosition: Vector2.zero);

                    return searchButton.rectTransform;
                });

                foreach (LovenseRemoteToy toy in selectedToys)
                {
                    toySettingsPage.AddElementToScrollView(scrollView =>
                    {
                        // Updated to pass a boolean parameter to the Action<bool> delegate
                        var toyButton = MenuAPI.CreateREPOToggle($"{toy.nickName} ({toy.name})", (isToggled) => ToggleToy(toy, isToggled), scrollView, defaultValue: true);

                        return toyButton.rectTransform;
                    });
                }

                // Button
                var settingsButton = MenuAPI.CreateREPOButton("Toy Support", () => toySettingsPage.OpenPage(openOnTop: true), parent, localPosition: Vector2.zero);
            });

            Patch();

            Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
        }

        private void Update()
        {
            var grabbers = Object.FindObjectsOfType<PhysGrabber>().Where(g => g.isLocal).ToList();

            if (grabbers.Count == 0)
            {
                SendStopCommandsToToys();
                lastGrab = null; // Clear last grab if no grabbers are found
                return;
            }

            foreach (PhysGrabber grabber in grabbers)
            {
                if ((Object)(object)grabber == (Object)null)
                {
                    SendStopCommandsToToys();
                    lastGrab = null; // Clear last grab if grabber is null

                    continue;
                }
                PhysGrabObject grabbedObject = grabber.grabbedPhysGrabObject;
                if ((Object)(object)grabbedObject == (Object)null)
                {
                    SendStopCommandsToToys();
                    lastGrab = null; // Clear last grab if no object is grabbed

                    continue;
                }
            }

            if (lastGrab)
            {
                if (lastGrab.dead || lastGrab.rb == null || lastGrab.rb.isKinematic || lastGrab.rb.mass <= 0f || !lastGrab.grabbed || !lastGrab.grabbedLocal)
                {
                    SendStopCommandsToToys();
                    lastGrab = null; // Clear last grab if the grabbed object is dead, kinematic, or has no mass
                }
            }
            else
            {
                // If no object is grabbed, ensure toys are stopped
                SendStopCommandsToToys();
            }
        }

        private void DoSearchToys(string domain, int port)
        {
            //The default value of useHttps is true, if set value to false, http will be used for data transfer.
            Logger.LogInfo($"Searching for toys at {domain}:{port}");
            LovenseRemote.GetInstance().GetToys(domain, port, isUseHttps: true, timeout: 30);
        }

        internal void Patch()
        {
            Harmony ??= new Harmony(Info.Metadata.GUID);
            Harmony.PatchAll(typeof(REPOToySupport));
        }

        internal void Unpatch()
        {
            Harmony?.UnpatchSelf();
        }

        private IEnumerator UpdateValue(PhysGrabObject obj)
        {
            SendStopCommandsToToys();
            yield return new WaitForSeconds(0.1f); // Wait a bit before sending the new value command
            SendCommmandToToys(obj); // Send the command to toys with the updated value

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
        private static void Hurt_Patch(PlayerHealth __instance, int damage)
        {
            if (__instance.invincibleTimer > 0f || (GameManager.Multiplayer() && !__instance.photonView.IsMine) || __instance.playerAvatar.deadSet || __instance.godMode)
            {
                return;
            }

            if (Instance.vibrateOnHurt.Value && Instance.selectedToys.Count > 0)
            {
                // Vibrate toys when the player is hurt
                float clampedDamage = Mathf.Clamp(damage, 0f, 1f); // Ensure damage is within a reasonable range    
                Logger.LogInfo($"Player hurt with damage {clampedDamage}. Vibrating toys.");
                float strength = clampedDamage * (100f * Instance.vibrationMax.Value); // Scale damage to a vibration strength
                Logger.LogInfo($"Player hurt with damage {damage}. Sending vibration command with strength {strength}.");
                LovenseRemote.GetInstance().SendPattern(Instance.selectedToys, new List<LovenseCommandType> { LovenseCommandType.VIBRATE }, new List<int> { (int)strength }, 2f);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.PhysGrabStarted))]
        private static void PhysGrabStarted_Patch(PhysGrabber __instance)
        {
            var obj = __instance.GetGrabbedObject();

            if ((bool)obj)
            {
                var physGrabObject = obj;
                if (physGrabObject != null)
                {
                    if (Instance.lastGrab != null && Instance.lastGrab == physGrabObject)
                    {
                        return; // Ignore duplicate grabs
                    }

                    Logger.LogInfo($"PhysGrabber started grabbing object: {physGrabObject.name}");
                    // Send command to toys when a grab starts
                    Instance.SendCommmandToToys(physGrabObject);
                    Instance.lastGrab = physGrabObject; // Update last grab to the current object
                }
                else
                {
                    Logger.LogWarning("PhysGrabber started but grabbed object is null.");
                    Instance.SendStopCommandsToToys();
                }
            }
            else
            {
                Logger.LogWarning("PhysGrabber started but no object was grabbed.");
                Instance.SendStopCommandsToToys();
            }
        }

        //[HarmonyPostfix]
        //[HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.PhysGrabEnded))]
        //private static void PhysGrabEnded_Patch(PhysGrabber __instance)
        //{
        //    var obj = __instance.GetGrabbedObject();
        //    if ((bool)obj)
        //    {
        //        var physGrabObject = obj;
        //        if (physGrabObject != null)
        //        {
        //            Logger.LogInfo($"PhysGrabber ended grabbing object: {physGrabObject.name}");
        //            // Send stop command to toys when a grab ends
        //            Instance.SendStopCommandsToToys();
        //            Instance.lastGrab = null; // Clear last grab since the grab has ended
        //        }
        //    }
        //    else
        //    {
        //        Logger.LogWarning("PhysGrabber ended but no object was grabbed.");
        //    }
        //}

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.ImpactLight))]
        private static void ImpactLight_Patch(PhysGrabObjectImpactDetector __instance)
        {
            if (Instance.vibrationSetting.Value != "impact")
                return;

            // We're going to temporarily disable light impacts, since they seem to be too frequent and annoying
            return;

            if (!__instance.isCart && !__instance.inCart && __instance.physGrabObject.heldByLocalPlayer) 
            {
                var value = __instance.physGrabObject.isValuable ? __instance.valuableObject.dollarValueCurrent : 0;

                // Use a simple algorithm to determine strength based on the value of the object
                float strength = 0.25f * (100f * Instance.vibrationMax.Value); // Light hits should vibrate at quarter strength

                Logger.LogInfo($"Light impact detected on object '{__instance.physGrabObject.name}'. Sending impact command with strength {strength}.");
                // Send impact command to toys with the calculated strength
                Instance.SendImpactCommandToToys((int)strength);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.ImpactMedium))]
        private static void ImpactMedium_Patch(PhysGrabObjectImpactDetector __instance)
        {
            if (Instance.vibrationSetting.Value != "impact" && Instance.vibrationSetting.Value != "value")
                return;

            if (!__instance.isCart && !__instance.inCart && __instance.physGrabObject.heldByLocalPlayer)
            {
                var value = __instance.physGrabObject.isValuable ? __instance.valuableObject.dollarValueCurrent : 0;

                if (Instance.vibrationSetting.Value == "value")
                {
                    var coroutine = Instance.UpdateValue(__instance.physGrabObject);
                    Instance.StartCoroutine(coroutine);
                    return;
                }

                // Use a simple algorithm to determine strength based on the value of the object
                float strength = 0.25f * (100f * Instance.vibrationMax.Value); // Medium hits should vibrate at half strength

                Logger.LogInfo($"Medium impact detected on object '{__instance.physGrabObject.name}'. Sending impact command with strength {strength}.");
                // Send impact command to toys with the calculated strength
                Instance.SendImpactCommandToToys((int)strength);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.ImpactHeavy))]
        private static void ImpactHeavy_Patch(PhysGrabObjectImpactDetector __instance)
        {
            if (Instance.vibrationSetting.Value != "impact" && Instance.vibrationSetting.Value != "value")
                return;

            if (!__instance.isCart && !__instance.inCart && __instance.physGrabObject.heldByLocalPlayer)
            {
                var value = __instance.physGrabObject.isValuable ? __instance.valuableObject.dollarValueCurrent : 0;

                if (Instance.vibrationSetting.Value == "value")
                {
                    var coroutine = Instance.UpdateValue(__instance.physGrabObject);
                    Instance.StartCoroutine(coroutine);
                    return;
                }

                // Use a simple algorithm to determine strength based on the value of the object
                float strength = 0.5f * (100f * Instance.vibrationMax.Value); // Heavy hits should vibrate at full strength

                Logger.LogInfo($"Heavy impact detected on object '{__instance.physGrabObject.name}'. Sending impact command with strength {strength}.");
                // Send impact command to toys with the calculated strength
                Instance.SendImpactCommandToToys((int)strength);
            }
        }

        private void SendStopCommandsToToys()
        {
            LovenseRemote.GetInstance().SendStopFunction(selectedToys);
        }

        private void SendImpactCommandToToys(int strength)
        {
            if (selectedToys.Count == 0)
            {
                Logger.LogWarning("No toys selected or grabbed object is null, cannot send impact command.");
                return;
            }

            var calculatedStrength = Mathf.Clamp(strength, 0, vibrationMax.Value) * 100f; // Ensure strength is scaled to the max vibration setting

            Logger.LogInfo($"Sending impact command to toys with strength: {calculatedStrength}%");
            LovenseRemote.GetInstance().SendPattern(selectedToys, new List<LovenseCommandType> { LovenseCommandType.VIBRATE }, new List<int> { (int)calculatedStrength, 0 }, 1f, intervalsMs: 100, timeout: 1);
            var stopCoroutine = ImpactStopCoroutine(1f); // Start the coroutine to stop the vibration after a delay
            StartCoroutine(stopCoroutine);
        }

        IEnumerator ImpactStopCoroutine(float delay)
        {
            // Wait for the specified delay before stopping the vibration
            yield return new WaitForSeconds(delay);
            // Stop the vibration
            SendStopCommandsToToys();
        }

        private void SendCommmandToToys(PhysGrabObject obj)
        {
            var value = 0f;
            var valueMax = 0f;

            if (obj.isValuable && vibrationSetting.Value == "value")
            {
                value = obj.GetComponent<ValuableObject>().dollarValueCurrent;
                valueMax = Object.FindObjectsOfType<ValuableObject>().Max(v => v.dollarValueCurrent);
            }
            else if (obj.isValuable && vibrationSetting.Value == "weight")
            {
                value = obj.rb.mass;
                valueMax = Object.FindObjectsOfType<PhysGrabObject>().Max(o => o.rb.mass);
            }
            else if (vibrationSetting.Value == "impact")
            {
                return;
            }
            else if (!obj.isValuable)
            {
                Logger.LogWarning($"Object '{obj.name}' is not a valuable or does not support the vibration setting '{vibrationSetting.Value}'. No command sent to toys.");
                return;
            }
            else
            {
                Logger.LogWarning($"Unknown vibration setting: {vibrationSetting.Value}. No command sent to toys.");
                return;
            }

                float strengthPercentage = Mathf.Clamp(value / valueMax, 0f, 1f) * (100f * vibrationMax.Value);

            Logger.LogInfo($"Sending command to toys with strength percentage: {strengthPercentage}% for object '{obj.name}' with value {value}. (Max scene value: {valueMax})");
            if (selectedToys.Count == 0)
            {
                Logger.LogWarning("No toys selected to send command to.");
                return;
            }
            // Send command to all selected toys
            LovenseRemote.GetInstance().SendPattern(selectedToys, new List<LovenseCommandType> { LovenseCommandType.VIBRATE }, new List<int> { (int)strengthPercentage }, vibrationTimeout.Value);
            //LovenseRemote.GetInstance().SendPreset(selectedToys, "wave", 100f);
        }

        private void OnSearchLocalApp(List<RemoteAppData> appDatas)
        {
            allAppDatas = appDatas;
            for (int i = 0; i < appDatas.Count; i++)
            {
            }
        }

        public void OnGetToys(List<LovenseRemoteToy> toys)
        {
            foreach (LovenseRemoteToy toy in toys)
            {
                Logger.LogInfo($"Found toy: {toy.name} (ID: {toy.id})");

                toySettingsPage.AddElementToScrollView(scrollView =>
                {
                    // Updated to pass a boolean parameter to the Action<bool> delegate
                    var toyButton = MenuAPI.CreateREPOToggle($"{toy.nickName} ({toy.name})", (isToggled) => ToggleToy(toy, isToggled), scrollView);

                    return toyButton.rectTransform;
                });
            }
        }

        public void ToggleToy(LovenseRemoteToy toToggle, bool isToggled)
        {
            if (isToggled)
            {
                if (selectedToys.Contains(toToggle))
                {
                    return; // Already selected, do nothing
                }
                else
                {
                    // If not selected, add to the list
                    selectedToys.Add(toToggle);

                    // Buzz the toy as an example action
                    LovenseRemote.GetInstance().SendPattern(selectedToys, new List<LovenseCommandType> { LovenseCommandType.VIBRATE }, new List<int> { 10, 5 }, 2, intervalsMs: 150, timeout: 5);
                }
            }
            else
            {
                if (!selectedToys.Contains(toToggle))
                {
                    return; // Not selected, do nothing
                }
                else
                {
                    // If selected, remove from the list
                    selectedToys.Remove(toToggle);
                }
            }
        }

        public void OnGetToysError(string errorMsg)
        {
            ShowError(errorMsg);
        }

        public void OnGetAppError(string errorMsg)
        {
            ShowError(errorMsg);
        }

        private void OnControlToysResult(string id, ControlToysResult result)
        {

            if (result.code != 200)
            {
                if (result.code == 500)
                {
                    ShowError("code:" + result.code + ",HTTP server not started or disabled.");
                }
                else if (result.code == 400)
                {
                    ShowError("code:" + result.code + ",Invalid Command.");
                }
                else if (result.code == 401)
                {
                    ShowError("code:" + result.code + ",Toy Not Found.");
                }
                else if (result.code == 402)
                {
                    ShowError("code:" + result.code + ",Toy Not Connected.");
                }
                else if (result.code == 403)
                {
                    ShowError("code:" + result.code + ",Toy Doesn't Support This Command.");
                }
                else if (result.code == 404)
                {
                    ShowError("code:" + result.code + ",Invalid Parameter.");
                }
                else if (result.code == 506)
                {
                    ShowError("code:" + result.code + ",Server Error.Restart Lovense Connect.");
                }
                else
                {
                    ShowError("code:" + result.code + "," + result.type);
                }
            }
        }

        public void ShowError(string errorMsg)
        {
            Debug.LogError("Toy Support Error: " + errorMsg);
            // You can also show a UI popup or notification here if needed
        }
    }
}

namespace REPOToySupport.Patches
{
    public static class PhysGrabberAccess
    {
        private static readonly FieldInfo grabbedObjField;

        static PhysGrabberAccess()
        {
            grabbedObjField = AccessTools.Field(typeof(PhysGrabber), "grabbedPhysGrabObject");
        }

        public static PhysGrabObject GetGrabbedObject(this PhysGrabber instance)
        {
            object? value = grabbedObjField.GetValue(instance);
            return (PhysGrabObject)((value is PhysGrabObject) ? value : null);
        }
    }
}