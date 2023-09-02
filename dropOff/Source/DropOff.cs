﻿using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

public enum DropOffType : byte
{
    Stack = 0
}

internal class DropOff
{
    public static int stackRadius = 7;
    public static XUiC_Backpack playerBackpack;
    public static XUiC_BackpackWindow backpackWindow;
    public static XUiC_ContainerStandardControls playerControls;
    public static int customLockEnum = (int)XUiC_ItemStack.LockTypes.Burning + 1; //XUiC_ItemStack.LockTypes - Last used is Burning with value 5, so we use 6 for our custom locked slots
    public static KeyCode[] dropOffLockHotkeys;
    public static KeyCode[] dropOffHotkeys;

    public static string LockedSlotsFile()
    {
        if (ConnectionManager.Instance.IsSinglePlayer)
            return Path.Combine(GameIO.GetPlayerDataDir(), GameManager.Instance.persistentLocalPlayer.UserIdentifier + ".qsls");
        return Path.Combine(GameIO.GetPlayerDataLocalDir(), GameManager.Instance.persistentLocalPlayer.UserIdentifier + ".qsls");
    }

    public static Dictionary<TileEntity, int> GetOpenedTiles()
    {
        return Traverse.Create(GameManager.Instance).Field("lockedTileEntities").GetValue<Dictionary<TileEntity, int>>();
    }

    // Checks if a loot container is openable by a player
    // HOST OR SERVER ONLY
    public static bool IsContainerUnlocked(int _entityIdThatOpenedIt, TileEntity _tileEntity)
    {
        if (!ConnectionManager.Instance.IsServer || _tileEntity == null)
        {
            return false;
        }

        // Handle locked containers
        if ((_tileEntity is TileEntitySecureLootContainer lootContainer) && lootContainer.IsLocked())
        {
            // Handle Host
            if (!GameManager.IsDedicatedServer && _entityIdThatOpenedIt == GameManager.Instance.World.GetPrimaryPlayerId())
            {
                if (!lootContainer.IsUserAllowed(GameManager.Instance.persistentLocalPlayer.UserIdentifier))
                {
                    return false;
                }
            }
            else
            {
                // Handle Client
                var cinfo = ConnectionManager.Instance.Clients.ForEntityId(_entityIdThatOpenedIt);
                if (cinfo == null || !lootContainer.IsUserAllowed(cinfo.CrossplatformId))
                {
                    return false;
                }
            }
        }

        var openTileEntities = GetOpenedTiles();

        // Handle in-use containers
        if (openTileEntities.ContainsKey(_tileEntity) &&
            (GameManager.Instance.World.GetEntity(openTileEntities[_tileEntity]) is EntityAlive entityAlive) &&
            !entityAlive.IsDead())
        {
            return false;
        }

        return true;
    }

    public static bool IsValidLoot(TileEntityLootContainer _tileEntity)
    {
        return (_tileEntity.GetTileEntityType() == TileEntityType.Loot ||
                _tileEntity.GetTileEntityType() == TileEntityType.SecureLoot ||
                _tileEntity.GetTileEntityType() == TileEntityType.SecureLootSigned);
    }

    // Yields all openable loot containers in a cubic radius about a point
    public static IEnumerable<ValueTuple<Vector3i, TileEntityLootContainer>> FindNearbyLootContainers(Vector3i _center, int _playerEntityId)
    {
        if (stackRadius > 127 || stackRadius < -127)
        {
            stackRadius = 127;
        }

        for (int i = -stackRadius; i <= stackRadius; i++)
        {
            for (int j = -stackRadius; j <= stackRadius; j++)
            {
                for (int k = -stackRadius; k <= stackRadius; k++)
                {
                    var offset = new Vector3i(i, j, k);
                    if (!(GameManager.Instance.World.GetTileEntity(0, _center + offset) is TileEntityLootContainer tileEntity))
                        continue;

                    if (IsValidLoot(tileEntity) && IsContainerUnlocked(_playerEntityId, tileEntity))
                    {
                        yield return new ValueTuple<Vector3i, TileEntityLootContainer>(offset, tileEntity);
                    }
                }
            }
        }
    }

    //DropOff functionality
    // SINGLEPLAYER ONLY
    public static void MoveDropOff()
    {
        if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.entityId == -1)
            return;

        var moveKind = XUiM_LootContainer.EItemMoveKind.FillAndCreate;

        EntityPlayerLocal primaryPlayer = GameManager.Instance.World.GetPrimaryPlayer();
        int lockedSlots = Traverse.Create(playerControls).Field("stashLockedSlots").GetValue<int>();

        //returns tile entities opened by other players
        Dictionary<TileEntity, int> openedTileEntities = GetOpenedTiles();

        for (int i = -stackRadius; i <= stackRadius; i++)
        {
            for (int j = -stackRadius; j <= stackRadius; j++)
            {
                for (int k = -stackRadius; k <= stackRadius; k++)
                {
                    Vector3i blockPos = new Vector3i((int)primaryPlayer.position.x + i, (int)primaryPlayer.position.y + j, (int)primaryPlayer.position.z + k);
                    TileEntityLootContainer tileEntity = GameManager.Instance.World.GetTileEntity(0, blockPos) as TileEntityLootContainer;

                    if (tileEntity == null)
                        continue;

                    //TODO: !tileEntity.IsUserAccessing() && !openedTileEntities.ContainsKey(tileEntity) does not work on multiplayer
                    if (IsValidLoot(tileEntity) && !tileEntity.IsUserAccessing() && !openedTileEntities.ContainsKey(tileEntity))
                    {
                        StashItems(playerBackpack, tileEntity, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
                        tileEntity.SetModified();
                    }
                }
            }
        }
    }

    public static void ClientMoveDropOff(Vector3i center, IEnumerable<Vector3i> _entityContainers)
    {
        if (backpackWindow.xui.lootContainer != null && backpackWindow.xui.lootContainer.entityId == -1)
            return;

        var moveKind = XUiM_LootContainer.EItemMoveKind.FillAndCreate;

        if (_entityContainers == null)
        {
            return;
        }
        int lockedSlots = Traverse.Create(playerControls).Field("stashLockedSlots").GetValue<int>();

        foreach (var offset in _entityContainers)
        {
            if (!(GameManager.Instance.World.GetTileEntity(0, center + offset) is TileEntityLootContainer tileEntity))
            {
                continue;
            }

            StashItems(playerBackpack, tileEntity, lockedSlots, moveKind, playerControls.MoveStartBottomRight);
            tileEntity.SetModified();
        }
    }

    //Refactored from the original code to check for custom locks
    public static ValueTuple<bool, bool> StashItems(XUiC_ItemStackGrid _srcGrid, IInventory _dstInventory, int _ignoredSlots, XUiM_LootContainer.EItemMoveKind _moveKind, bool _startBottomRight)
    {
        if (_srcGrid == null || _dstInventory == null)
        {
            return new ValueTuple<bool, bool>(false, false);
        }
        XUiController[] itemStackControllers = _srcGrid.GetItemStackControllers();

        bool item = true;
        bool item2 = false;
        int num = _startBottomRight ? (itemStackControllers.Length - 1) : _ignoredSlots;
        while (_startBottomRight ? (num >= _ignoredSlots) : (num < itemStackControllers.Length))
        {
            XUiC_ItemStack xuiC_ItemStack = (XUiC_ItemStack)itemStackControllers[num];
            if (!xuiC_ItemStack.StackLock && Traverse.Create(xuiC_ItemStack).Field("lockType").GetValue<int>() != DropOff.customLockEnum)
            {
                ItemStack itemStack = xuiC_ItemStack.ItemStack;
                if (!xuiC_ItemStack.ItemStack.IsEmpty())
                {
                    int count = itemStack.count;
                    _dstInventory.TryStackItem(0, itemStack);
                    if (itemStack.count > 0 && (_moveKind == XUiM_LootContainer.EItemMoveKind.All || (_moveKind == XUiM_LootContainer.EItemMoveKind.FillAndCreate && _dstInventory.HasItem(itemStack.itemValue))) && _dstInventory.AddItem(itemStack))
                    {
                        itemStack = ItemStack.Empty.Clone();
                    }
                    if (itemStack.count == 0)
                    {
                        itemStack = ItemStack.Empty.Clone();
                    }
                    else
                    {
                        item = false;
                    }
                    if (count != itemStack.count)
                    {
                        xuiC_ItemStack.ForceSetItemStack(itemStack);
                        item2 = true;
                    }
                }
            }
            num = (_startBottomRight ? (num - 1) : (num + 1));
        }

        return new ValueTuple<bool, bool>(item, item2);
    }

    // UI Delegates
    public static void DropOffOnClick()
    {
        // Singleplayer
        if (ConnectionManager.Instance.IsSinglePlayer)
        {
            MoveDropOff();
            // Multiplayer (Client)
        }
        else if (!ConnectionManager.Instance.IsServer)
        {
            ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageFindOpenableContainers>().Setup(GameManager.Instance.World.GetPrimaryPlayerId(), DropOffType.Stack));
            // Multiplayer (Host)
        }
        else if (!GameManager.IsDedicatedServer)
        {
            // But we do the steps of Multiplayer quick stack in-place because
            // The host has access to locking functions
            var player = GameManager.Instance.World.GetPrimaryPlayer();
            var center = new Vector3i(player.position);
            List<Vector3i> offsets = new List<Vector3i>(1024);
            foreach (var pair in FindNearbyLootContainers(center, player.entityId))
            {
                offsets.Add(pair.Item1);
            }
            ClientMoveDropOff(center, offsets);
        }
    }

    /* 
     * Binary format:
     * [int32] locked slots - mod compatibility
     * [int32] array count (N) of locked slots by us
     * [N bytes] boolean array indicating locked slots
     */
    public static void SaveLockedSlots()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            XUiController[] slots = playerBackpack.GetItemStackControllers();

            using (BinaryWriter binWriter = new BinaryWriter(File.Open(LockedSlotsFile(), FileMode.Create)))
            {
                binWriter.Write(Traverse.Create(playerControls).Field("stashLockedSlots").GetValue<int>());

                binWriter.Write(slots.Length);
                for (int i = 0; i < slots.Length; i++)
                    binWriter.Write(Traverse.Create(slots[i] as XUiC_ItemStack).Field("lockType").GetValue<int>() == customLockEnum);
            }
            Log.Out($"[DropOff] Saved locked slots config in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            Log.Error($"[DropOff] Failed to write locked slots file: {e.Message}. Slot states will not be saved!");
        }
    }

    public static void LoadLockedSlots()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string path = LockedSlotsFile();
            if (!File.Exists(path))
            {
                Log.Warning("[DropOff] No locked slots config detected. Slots will default to unlocked");
                return;
            }

            // reported number of locked slots
            long reportedLength = new FileInfo(path).Length - sizeof(int) * 2;
            if (reportedLength < 0)
            {
                // file is too small to process
                Log.Error("[DropOff] locked slots config appears corrupted. Slots will be defaulted to unlocked");
                return;
            }

            using (BinaryReader binReader = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                // locked slots saved by the unused combobox some mods may enable
                int comboLockedSlots = Math.Max(0, binReader.ReadInt32());

                // locked slots saved by us
                int dropOffLockedSlots = binReader.ReadInt32();
                if (reportedLength != dropOffLockedSlots * sizeof(bool))
                {
                    Log.Error("[DropOff] locked slots config appears corrupted. Slots will be defaulted to unlocked");
                    return;
                }

                // KHA20-LockableInvSlots compatibility
                if (playerControls.GetChildById("cbxLockedSlots") is XUiC_ComboBoxInt comboBox)
                {
                    comboBox.Value = comboLockedSlots;
                    playerControls.ChangeLockedSlots(comboLockedSlots);
                }

                XUiController[] slots = playerBackpack.GetItemStackControllers();
                for (int i = 0; i < Math.Min(dropOffLockedSlots, slots.Length); i++)
                {
                    if (binReader.ReadBoolean())
                        Traverse.Create(slots[i] as XUiC_ItemStack).Field("lockType").SetValue(customLockEnum);
                }
            }
            Log.Out($"[DropOff] Loaded locked slots config in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            Log.Error($"[DropOff] Failed to read locked slots config:  {e.Message}. Slots will default to unlocked");
        }
    }
}