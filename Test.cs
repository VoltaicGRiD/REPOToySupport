using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Lovense.UnityKit;
using Lovense.UnityKit.Remote;
using MenuLib;
using MenuLib.MonoBehaviors;
using Steamworks.ServerList;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal class ToySupportPatcher
{
    public static void DoPatching()
    {
        var harmony = new Harmony("com.voltaicgrid.toysupport");
        harmony.PatchAll();
    }
}

class ToySupport
{
    private List<RemoteAppData> allAppDatas;
    private RemoteAppData thisRemoteAppData;
    private LovenseRemoteToy thisToy;
    private List<LovenseRemoteToy> allToys = new List<LovenseRemoteToy>();
    private List<LovenseRemoteToy> selectedToys = new List<LovenseRemoteToy>();
    private List<PhysGrabber> grabbers = new List<PhysGrabber>();

    private REPOPopupPage toySettingsPage;

    private static ToySupport Instance;

    PhysGrabObject grabbedObject;

    private void Awake()
    {
        LovenseRemote.onGetAppsEvent.AddListener(OnSearchLocalApp);
        LovenseRemote.onGetAppsErrorEvent.AddListener(OnGetAppError);
        LovenseRemote.onGetToysEvent.AddListener(OnGetToys);
        LovenseRemote.onGetToysErrorEvent.AddListener(OnGetToysError);
        LovenseRemote.onControlToysEvent.AddListener(OnControlToysResult);

        MenuAPI.AddElementToSettingsMenu(parent =>
        {
            // Settings page
            toySettingsPage = MenuAPI.CreateREPOPopupPage("Toy Support Settings", REPOPopupPage.PresetSide.Left, shouldCachePage: true);

            toySettingsPage.AddElementToScrollView(scrollView =>
            {
                var searchButton = MenuAPI.CreateREPOButton("Search", DoSearch, scrollView, localPosition: Vector2.zero);

                return searchButton.rectTransform;
            });



            // Button
            var settingsButton = MenuAPI.CreateREPOButton("Toy Support", () => toySettingsPage.OpenPage(openOnTop: true), parent, localPosition: Vector2.zero);
        });

        if (Instance == null)
        {
            Instance = this;
        }

        ScanForGrabbers();
    }



    private void DoSearch()
    {
        LovenseRemote.GetInstance().SearchRemoteApp();
    }

    [HarmonyPatch(typeof(PhysGrabber))]
    [HarmonyPatch("PhysGrabBeamActivate")]
    void PhysGrabBeamActivate_Patch()
    {

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
            toySettingsPage.AddElementToScrollView(scrollView =>
            {
                // Updated to pass a boolean parameter to the Action<bool> delegate
                var toyButton = MenuAPI.CreateREPOToggle(toy.name, (isToggled) => ToggleToy(toy, isToggled), scrollView);

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