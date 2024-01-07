using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using I2.Loc;
using UnityEngine.EventSystems;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using static ItemCompressor.PatchMethods;


namespace ItemCompressor
{
    public class Main : Mod
    {
        static Dictionary<string, Item_Base> itemLookup = new Dictionary<string, Item_Base>();
        public static Item_Base LookupItem(string name)
        {
            if (!itemLookup.TryGetValue(name, out var item) || !item)
                item = itemLookup[name] = ItemManager.GetItemByName(name);
            return item;
        }
        static bool started;
        Harmony harmony;
        public static LanguageSourceData language;
        public static List<Object> createdObjects = new List<Object>();
        Transform prefabHolder;
        public static Item_Base compressedItem;
        public static Item_Base compressorItem;
        static ModData entry;
        public static string storedKey = "CompressedItem";
        static Mod cachingMod;
        public static int compressorCapacity = 20;
        public static int CompressorCapacity => ExtraSettingsAPI_Loaded ? compressorCapacity : 20;
        static bool CanUnload
        {
            get { return !entry.jsonmodinfo.isModPermanent; }
            set
            {
                if (value != CanUnload)
                {
                    entry.jsonmodinfo.isModPermanent = !value;
                    ModManagerPage.RefreshModState(entry);
                }
            }
        }
        public void Start()
        {
            entry = modlistEntry;
            started = false;
            if (SceneManager.GetActiveScene().name == Raft_Network.GameSceneName && ComponentManager<Raft_Network>.Value.remoteUsers.Count > 1)
            {
                entry.modinfo.unloadBtn.GetComponent<Button>().onClick.Invoke();
                throw new ModLoadException("Mod cannot be loaded on while in a multiplayer");
            }
            started = true;
            prefabHolder = new GameObject("prefabHolder").transform;
            prefabHolder.gameObject.SetActive(false);
            createdObjects.Add(prefabHolder.gameObject);
            DontDestroyOnLoad(prefabHolder.gameObject);
            language = new LanguageSourceData()
            {
                mDictionary = new Dictionary<string, TermData>(),
                mLanguages = new List<LanguageData> { new LanguageData() { Code = "en", Name = "English" } }
            };
            LocalizationManager.Sources.Add(language);
            (harmony = new Harmony("com.aidanamite.ItemCompressor")).PatchAll();


            var plank = LookupItem("Plank");
            compressorItem = plank.Clone(17178, "Storage_ItemCompressor");
            compressorItem.settings_Inventory.LocalizationTerm = "Item/" + compressorItem.UniqueName;
            language.mDictionary.Add(compressorItem.settings_Inventory.LocalizationTerm, new TermData() { Languages = new[] { "Item Compressor@A useful tool for storing a lot of one item." } });
            Traverse.Create(compressorItem.settings_Inventory).Field("stackSize").SetValue(10);
            compressorItem.settings_Inventory.Sprite = LoadImage("itemIcon.png", false).ToSprite();
            compressorItem.SetRecipe(new[]
            {
                new CostMultiple(new[] { LookupItem("Glass") }, 4),
                new CostMultiple(new[] { LookupItem("TitaniumIngot") }, 2),
                new CostMultiple(new[] { LookupItem("MetalIngot") }, 6),
                new CostMultiple(new[] { LookupItem("Color_Black") }, 5),
                new CostMultiple(new[] { LookupItem("Battery") }, 1)
            }, CraftingCategory.Tools);
            RAPI.RegisterItem(compressorItem);

            compressedItem = plank.Clone(17179, "Storage_CompressedItem");
            compressedItem.settings_Inventory.LocalizationTerm = "Item/" + compressedItem.UniqueName;
            language.mDictionary.Add(compressedItem.settings_Inventory.LocalizationTerm, new TermData() { Languages = new[] { "Item Compressor ({0})@A useful tool for storing a lot of {0}." } });
            Traverse.Create(compressedItem.settings_Inventory).Field("stackSize").SetValue(1);
            Traverse.Create(compressedItem).Field("maxUses").SetValue((int)short.MaxValue); 
            Traverse.Create(compressedItem.settings_consumeable).Field("itemAfterUse").SetValue(new Cost(compressorItem,1));
            Traverse.Create(compressedItem).Field("barGradient").SetValue(new Gradient());
            compressedItem.BarGradient.SetKeys(new[] { new GradientColorKey(Color.gray, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            compressedItem.settings_Inventory.Sprite = LoadImage("itemIcon.png", false).ToSprite();
            RAPI.RegisterItem(compressedItem);

            Traverse.Create(typeof(LocalizationManager)).Field("OnLocalizeEvent").GetValue<LocalizationManager.OnLocalizeCallback>().Invoke();
            Log("Mod has been loaded!");
        }

        static bool ExtraSettingsAPI_Loaded = false;
        public void ExtraSettingsAPI_Load() => ExtraSettingsAPI_SettingsClose();
        public void ExtraSettingsAPI_SettingsClose()
        {
            if (!int.TryParse(ExtraSettingsAPI_GetInputValue("CompressorCapacity"), out var res))
                res = 20;
            if (res < 2)
            {
                ExtraSettingsAPI_SetInputValue("CompressorCapacity", "2");
                res = 2;
            }
            compressorCapacity = res;
        }

        public virtual string ExtraSettingsAPI_GetInputValue(string SettingName) => "".ToString();
        public virtual void ExtraSettingsAPI_SetInputValue(string SettingName, string NewValue) { }

        public void OnModUnload()
        {
            if (!started)
                return;
            harmony?.UnpatchAll(harmony.Id);
            LocalizationManager.Sources.Remove(language);
            foreach (var o in createdObjects)
            {
                if (o is Item_Base i)
                    ItemManager.GetAllItems().Remove(i);
                if (o)
                    Destroy(o);
            }
            createdObjects.Clear();
            Log("Mod has been unloaded!");
        }

        static void CheckUnload() => CanUnload = SceneManager.GetActiveScene().name != Raft_Network.GameSceneName || ComponentManager<Raft_Network>.Value.remoteUsers.Count <= 1;
        public override void WorldEvent_OnPlayerConnected(CSteamID steamid, RGD_Settings_Character characterSettings) => CheckUnload();
        public override void WorldEvent_OnPlayerDisconnected(CSteamID steamid, DisconnectReason disconnectReason) => CheckUnload();
        public override void WorldEvent_WorldLoaded() => CheckUnload();
        public override void WorldEvent_WorldUnloaded() => CheckUnload();

        public Texture2D LoadImage(string filename, bool generateMipMaps = true, FilterMode? mode = null, bool leaveReadable = true)
        {
            var t = new Texture2D(0, 0, TextureFormat.RGBA32, generateMipMaps);
            t.LoadImage(GetEmbeddedFileBytes(filename), !leaveReadable);
            if (mode != null)
                t.filterMode = mode.Value;
            if (leaveReadable)
                t.Apply();
            createdObjects.Add(t);
            return t;
        }

        public void ModUtils_ModLoaded(Mod other)
        {
            if (other.GetType().Name == "InventoryCaching")
                cachingMod = other;
        }
        public static void ClearItemCache(string item)
        {
            if (cachingMod)
                Traverse.Create(cachingMod).Method("ClearItemCache", item).GetValue();
        }
    }
    class ModLoadException : Exception
    {
        public ModLoadException(string message) : base(message) { }
    }

    static class ExtentionMethods
    {
        public static Item_Base Clone(this Item_Base source, int uniqueIndex, string uniqueName)
        {
            Item_Base item = ScriptableObject.CreateInstance<Item_Base>();
            item.Initialize(uniqueIndex, uniqueName, source.MaxUses);
            item.settings_buildable = source.settings_buildable.Clone();
            item.settings_consumeable = source.settings_consumeable.Clone();
            item.settings_cookable = source.settings_cookable.Clone();
            item.settings_equipment = source.settings_equipment.Clone();
            item.settings_Inventory = source.settings_Inventory.Clone();
            item.settings_recipe = source.settings_recipe.Clone();
            item.settings_usable = source.settings_usable.Clone();
            Main.createdObjects.Add(item);
            return item;
        }
        public static void SetRecipe(this Item_Base item, CostMultiple[] cost, CraftingCategory category = CraftingCategory.Resources, int amountToCraft = 1, bool learnedFromBeginning = false, string subCategory = null, int subCatergoryOrder = 0)
        {
            Traverse recipe = Traverse.Create(item.settings_recipe);
            recipe.Field("craftingCategory").SetValue(category);
            recipe.Field("amountToCraft").SetValue(amountToCraft);
            recipe.Field("learnedFromBeginning").SetValue(learnedFromBeginning);
            recipe.Field("subCategory").SetValue(subCategory);
            recipe.Field("subCatergoryOrder").SetValue(subCatergoryOrder);
            item.settings_recipe.NewCost = cost;
        }

        public static Sprite ToSprite(this Texture2D texture, Rect? rect = null, Vector2? pivot = null)
        {
            var s = Sprite.Create(texture, rect ?? new Rect(0, 0, texture.width, texture.height), pivot ?? new Vector2(0.5f, 0.5f));
            Main.createdObjects.Add(s);
            return s;
        }


        public static Texture2D GetReadable(this Texture2D source, Rect? copyArea = null, RenderTextureFormat format = RenderTextureFormat.Default, RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default, TextureFormat? targetFormat = null, bool mipChain = true)
        {
            var temp = RenderTexture.GetTemporary(source.width, source.height, 0, format, readWrite);
            Graphics.Blit(source, temp);
            temp.filterMode = FilterMode.Point;
            var prev = RenderTexture.active;
            RenderTexture.active = temp;
            var area = copyArea ?? new Rect(0, 0, temp.width, temp.height);
            area.y = temp.height - area.y - area.height;
            var texture = new Texture2D((int)area.width, (int)area.height, targetFormat ?? TextureFormat.RGBA32, mipChain);
            texture.ReadPixels(area, 0, 0);
            texture.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(temp);
            Main.createdObjects.Add(texture);
            return texture;
        }

        public static void Edit(this Texture2D baseImg, Texture2D overlay)
        {
            var w = baseImg.width - 1;
            var h = baseImg.height - 1;
            for (var x = 0; x <= w; x++)
                for (var y = 0; y <= h; y++)
                    baseImg.SetPixel(x, y, baseImg.GetPixel(x, y).Overlay(overlay.GetPixelBilinear((float)x / w, (float)y / h)));
            baseImg.Apply();
        }
        public static Color Overlay(this Color a, Color b)
        {
            if (a.a <= 0)
                return b;
            if (b.a <= 0)
                return a;
            var r = b.a / (b.a + a.a * (1 - b.a));
            float Ratio(float aV, float bV) => bV * r + aV * (1 - r);
            return new Color(Ratio(a.r, b.r), Ratio(a.g, b.g), Ratio(a.b, b.b), b.a + a.a * (1 - b.a));
        }

        public static string String(this byte[] bytes, int length = -1, int offset = 0)
        {
            string str = "";
            if (length == -1)
                length = (bytes.Length - offset) / 2;
            while (str.Length < length)
            {
                str += BitConverter.ToChar(bytes, offset + str.Length * 2);
            }
            return str;

        }
        public static string String(this List<byte> bytes) => bytes.ToArray().String();
        public static byte[] Bytes(this string str)
        {
            var data = new List<byte>();
            foreach (char chr in str)
                data.AddRange(BitConverter.GetBytes(chr));
            return data.ToArray();
        }
        public static int Integer(this byte[] bytes, int offset = 0) => System.BitConverter.ToInt32(bytes, offset);
        public static byte[] Bytes(this int value) => BitConverter.GetBytes(value);


        public static void SetValue(this ItemInstance item, string valueName, string value)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            var newData = new List<byte>(data);
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                pos += 4 + name.Length * 2;
                l = data.Integer(pos);
                var oldValue = data.String(l, pos + 4);
                if (name == valueName)
                {
                    newData.RemoveRange(pos, 4 + oldValue.Length * 2);
                    newData.InsertRange(pos, value.Bytes());
                    newData.InsertRange(pos, value.Length.Bytes());
                    break;
                }
                pos += 4 + oldValue.Length * 2;
            }
            if (pos >= data.Length)
            {
                newData.AddRange(valueName.Length.Bytes());
                newData.AddRange(valueName.Bytes());
                newData.AddRange(value.Length.Bytes());
                newData.AddRange(value.Bytes());
            }
            item.exclusiveString = newData.String();
        }

        public static string GetValue(this ItemInstance item, string valueName)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                pos += 4 + name.Length * 2;
                l = data.Integer(pos);
                var value = data.String(l, pos + 4);
                pos += 4 + value.Length * 2;
                if (name == valueName)
                    return value;
            }
            throw new MissingFieldException("No value \"" + valueName + "\" was found on the ItemInstance");
        }

        public static bool RemoveValue(this ItemInstance item, string valueName)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                var offset = 4 + name.Length * 2;
                l = data.Integer(pos + offset);
                var value = data.String(l, pos + offset + 4);
                offset += 4 + value.Length * 2;
                if (name == valueName)
                {
                    var newData = new List<byte>(data);
                    newData.RemoveRange(pos, offset);
                    item.exclusiveString = newData.String();
                    return true;
                }
                pos += offset;
            }
            return false;
        }

        public static Dictionary<string, string> GetData(this ItemInstance item)
        {
            int pos = 0;
            var data = item.exclusiveString.Bytes();
            var d = new Dictionary<string, string>();
            while (pos < data.Length)
            {
                var l = data.Integer(pos);
                var name = data.String(l, pos + 4);
                pos += 4 + name.Length * 2;
                l = data.Integer(pos);
                var value = data.String(l, pos + 4);
                pos += 4 + value.Length * 2;
                d.Add(name, value);
            }
            return d;
        }

        public static bool TryGetData(this ItemInstance item, out Dictionary<string, string> values)
        {
            try
            {
                values = item.GetData();
                return true;
            }
            catch
            {
                values = new Dictionary<string, string>();
                return false;
            }
        }

        public static void SetData(this ItemInstance item, Dictionary<string, string> values)
        {
            var data = new List<byte>();
            foreach (var pair in values)
            {
                data.AddRange(pair.Key.Length.Bytes());
                data.AddRange(pair.Key.Bytes());
                data.AddRange(pair.Value.Length.Bytes());
                data.AddRange(pair.Value.Bytes());
            }
            item.exclusiveString = data.String();
        }

        public static bool CanBeStored(this Item_Base item) => item && item.UniqueName != Main.compressedItem.UniqueName && item.UniqueName != Main.compressorItem.UniqueName && (item.settings_Inventory.StackSize > 1 || item.MaxUses == 1);

        public static void ChangeUses(this Slot slot, int UsesToAdd, bool AddItemAfterUse = true)
        {
            if (!slot || !slot.HasValidItemInstance() || UsesToAdd == 0)
                return;
            var total = slot.itemInstance.UsesInStack;
            var after = AddItemAfterUse && slot.itemInstance.baseItem.settings_consumeable.ItemAfterUse?.item != null ? slot.itemInstance.baseItem.settings_consumeable.ItemAfterUse : null;
            var newTotal = total + UsesToAdd;
            var newAmount = Mathf.CeilToInt(newTotal / (float)slot.itemInstance.BaseItemMaxUses);
            var lostAmount = slot.itemInstance.Amount - newAmount;
            var newUses = (newTotal - 1) % slot.itemInstance.BaseItemMaxUses + 1;
            var inv = Traverse.Create(slot).Field("inventory").GetValue<Inventory>();
            if (newAmount > slot.itemInstance.settings_Inventory.StackSize)
            {
                slot.itemInstance.Amount = slot.itemInstance.settings_Inventory.StackSize;
                slot.itemInstance.Uses = slot.itemInstance.BaseItemMaxUses;
                slot.RefreshComponents();
                var c = newAmount - slot.itemInstance.settings_Inventory.StackSize;
                while (c > 0)
                {
                    inv.AddItem(new ItemInstance(
                        slot.itemInstance.baseItem,
                        Math.Min(newAmount, slot.itemInstance.settings_Inventory.StackSize),
                        c <= slot.itemInstance.settings_Inventory.StackSize ? newUses : slot.itemInstance.BaseItemMaxUses,
                        slot.itemInstance.exclusiveString));
                    c -= slot.itemInstance.settings_Inventory.StackSize;
                }
            }
            else if (newAmount <= 0)
                slot.SetItem(null);
            else
            {
                slot.itemInstance.Amount = newAmount;
                slot.itemInstance.Uses = newUses;
                slot.RefreshComponents();
            }
            if (AddItemAfterUse && after != null && lostAmount > 0)
            {
                var c = lostAmount * after.amount;
                var stack = after.item.settings_Inventory.StackSize;
                while (c > 0)
                {
                    if (slot.HasValidItemInstance())
                        inv.AddItem(new ItemInstance(after.item, Math.Min(c, stack), after.item.MaxUses));
                    else
                        slot.SetItem(new ItemInstance(after.item, Math.Min(c, stack), after.item.MaxUses));
                    c -= stack;
                }
            }
        }
    }

    [HarmonyPatch(typeof(LanguageSourceData), "GetLanguageIndex")]
    static class Patch_GetLanguageIndex
    {
        static void Postfix(LanguageSourceData __instance, ref int __result)
        {
            if (__result == -1 && __instance == Main.language)
                __result = 0;
        }
    }

    /*[HarmonyPatch(typeof(Slot), "RefreshComponents")]
    static class Patch_RenderSlot
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var 
        }
    }*/

    static class PatchMethods
    {
        public static bool OverrideShouldTryStack(Slot fromSlot, Slot toSlot)
        {
            var c = new[] { Main.compressorItem.UniqueName, Main.compressedItem.UniqueName };
            var t1 = Array.IndexOf(c, fromSlot.GetItemBase().UniqueName);
            var t2 = Array.IndexOf(c, toSlot.GetItemBase().UniqueName);
            var n1 = t1 == 1 ? fromSlot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null : fromSlot.GetItemBase().UniqueName;
            var n2 = t2 == 1 ? toSlot.itemInstance.TryGetData(out var data2) && data2.TryGetValue(Main.storedKey, out var val2) ? val2 : null : toSlot.GetItemBase().UniqueName;
            if (n1 == null || n2 == null)
                return false;
            if (t1 == -1 && !fromSlot.GetItemBase().CanBeStored())
                return false;
            if (t2 == -1 && !toSlot.GetItemBase().CanBeStored())
                return false;
            if (t1 == -1)
            {
                if (t2 == -1)
                    return false;
                if (t2 == 0)
                    return toSlot.itemInstance.Amount == 1;
                if (t2 == 1)
                    return n1 == n2;
            }
            if (t1 == 0)
                return t2 == -1;
            if (t1 == 1)
                return t2 != 0 && n1 == n2;
            return false;
        }
        public static int OverrideAmount(int original, Slot item, PointerEventData.InputButton button)
        {
            if (item.GetItemBase().UniqueName == Main.compressedItem.UniqueName)
            {
                var name = item.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
                if (name != null)
                    return button == PointerEventData.InputButton.Left ? -1 : item.itemInstance.UsesInStack;
            }
            return original;
        }

        public static bool OverrideTryStack(Slot fromSlot, Slot toSlot, int amount)
        {
            var c = new[] { Main.compressorItem.UniqueName, Main.compressedItem.UniqueName };
            var t1 = Array.IndexOf(c, fromSlot.GetItemBase().UniqueName);
            var t2 = Array.IndexOf(c, toSlot.GetItemBase().UniqueName);
            var n1 = t1 == 1 ? fromSlot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null : fromSlot.GetItemBase().UniqueName;
            var n2 = t2 == 1 ? toSlot.itemInstance.TryGetData(out var data2) && data2.TryGetValue(Main.storedKey, out var val2) ? val2 : null : toSlot.GetItemBase().UniqueName;
            if (n1 == null || n2 == null)
                return false;
            if (t1 == -1)
            {
                if (t2 == 0 && toSlot.itemInstance.Amount == 1)
                {
                    var mov = Math.Min(fromSlot.itemInstance.UsesInStack, amount);
                    var i = new ItemInstance(Main.compressedItem, 1, mov);
                    i.SetValue(Main.storedKey, fromSlot.itemInstance.UniqueName);
                    toSlot.SetItem(i);
                    fromSlot.ChangeUses(-mov, false);
                    return true;
                }
                if (t2 == 1 && n1 == n2)
                {
                    var max = toSlot.itemInstance.BaseItemMaxUses * toSlot.itemInstance.settings_Inventory.StackSize - toSlot.itemInstance.UsesInStack;
                    var mov = Math.Min(Math.Min(fromSlot.itemInstance.UsesInStack, max), amount);
                    if (mov > 0)
                    {
                        fromSlot.ChangeUses(-mov, false);
                        toSlot.ChangeUses(mov);
                    }
                    return true;
                }
            }
            if (t1 == 0 && t2 == -1)
            {
                fromSlot.RemoveItem(1);
                var i = new ItemInstance(Main.compressedItem, 1, toSlot.itemInstance.UsesInStack);
                i.SetValue(Main.storedKey, toSlot.itemInstance.UniqueName);
                toSlot.SetItem(i);
                return true;
            }
            if (t1 == 1 && t2 != 0 && n1 == n2)
            {
                Main.ClearItemCache(n1);
                var max = toSlot.itemInstance.BaseItemMaxUses * toSlot.itemInstance.settings_Inventory.StackSize - toSlot.itemInstance.UsesInStack;
                var mov = Math.Min(fromSlot.itemInstance.UsesInStack, max);
                if (amount >= 0)
                    mov = Math.Min(mov, amount);
                if (mov > 0)
                {
                    fromSlot.ChangeUses(-mov);
                    toSlot.ChangeUses(mov);
                }
                return true;
            }
            return false;
        }
        
        public static bool OverrideTryMove(Slot fromSlot, Slot toSlot, int amount)
        {
            if (fromSlot.GetItemBase().UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name = fromSlot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null)
                return false;
            Main.ClearItemCache(name);
            if (amount < 0)
            {
                toSlot.SetItem(fromSlot.itemInstance);
                fromSlot.SetItem(null);
                return true;
            }
            var item = Main.LookupItem(name);
            var max = item ? item.MaxUses * item.settings_Inventory.StackSize : short.MaxValue;
            var mov = Math.Min(Math.Min(fromSlot.itemInstance.UsesInStack, max), amount);
            fromSlot.ChangeUses(-mov);
            if (mov > 0 && item)
                toSlot.SetItem(new ItemInstance(item, Mathf.CeilToInt(mov / (float)item.MaxUses), (mov - 1) % item.MaxUses + 1));
            return true;
        }

        public static bool CanQuickStack(Item_Base item, Slot targetSlot)
        {
            if (!item.CanBeStored())
                return false;
            if (item.UniqueName == Main.compressedItem.UniqueName || item.UniqueName == Main.compressorItem.UniqueName)
                return false;
            if (!targetSlot.HasValidItemInstance())
                return false;
            if (targetSlot.GetItemBase().UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name = targetSlot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null)
                return false;
            return name == item.UniqueName && targetSlot.itemInstance.UsesInStack < targetSlot.itemInstance.BaseItemMaxUses * targetSlot.itemInstance.settings_Inventory.StackSize;
        }
        public static void OnHoverSlot(PlayerInventory playerInventory, Slot enteredSlot)
        {
            if (enteredSlot && enteredSlot.HasValidItemInstance() && enteredSlot.GetItemBase().UniqueName == Main.compressedItem.UniqueName)
            {
                var name = enteredSlot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
                var item = name == null ? null : Main.LookupItem(name);
                playerInventory.itemNameText.text = string.Format(playerInventory.itemNameText.text, item ? item.settings_Inventory.DisplayName : "?????");
                playerInventory.itemDescriptionText.text = string.Format(playerInventory.itemDescriptionText.text, item ? item.settings_Inventory.DisplayName : "?????");
            }
        }
        public static string FormatDisplayName(string displayName, ItemInstance instance)
        {
            if (instance.baseItem.UniqueName == Main.compressedItem.UniqueName)
            {
                var name = instance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
                var item = name == null ? null : Main.LookupItem(name);
                return string.Format(displayName, item ? item.settings_Inventory.DisplayName : "?????");
            }
            return displayName;
        }

        public static int OverrideInstanceMax(int original, ItemInstance instance)
        {
            if (instance.baseItem.UniqueName != Main.compressedItem.UniqueName)
                return original;
            var name = instance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null)
                return original;
            var item = Main.LookupItem(name);
            if (item == null)
                return original;
            return (item.MaxUses < 1 ? 1 : item.MaxUses) * item.settings_Inventory.StackSize * Main.CompressorCapacity;
        }

        public static bool ForceStackNotFull(Slot slot)
        {
            if (slot.itemInstance.UniqueName != Main.compressedItem.UniqueName)
                return false;
            return slot.itemInstance.Uses < slot.itemInstance.BaseItemMaxUses || slot.itemInstance.Amount < slot.itemInstance.settings_Inventory.StackSize;
        }

        public static bool TryAddItem(Slot slot, Item_Base item, int amount)
        {
            if (!slot.HasValidItemInstance())
                return false;
            if (Main.compressedItem.UniqueName != slot.GetItemBase().UniqueName)
                return false;
            var name = slot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null || item.UniqueName != name)
                return false;
            slot.ChangeUses(amount);
            Main.ClearItemCache(name);
            return true;
        }

        public static bool TryCountByItem(Slot slot, Item_Base item, ref int count)
        {
            if (slot.itemInstance.UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name = slot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null || item.UniqueName != name)
                return false;
            count += slot.itemInstance.UsesInStack;
            return true;
        }

        public static bool TryCountByName(Slot slot, string item, ref int count)
        {
            if (slot.itemInstance.UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name = slot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null || item != name)
                return false;
            count += slot.itemInstance.UsesInStack;
            return true;
        }

        public static bool CancelDrop(Slot slot, PlayerInventory localPlayerInventory, int amount)
        {
            if (slot.itemInstance.UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name = slot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name == null)
                return false;
            var item = Main.LookupItem(name);
            if (!item || amount < 0)
            {
                localPlayerInventory.DropItem(slot);
                return true;
            }
            var mov = Math.Min(Math.Min(amount, slot.itemInstance.UsesInStack), item.MaxUses * item.settings_Inventory.StackSize);
            if (mov > 0)
            {
                slot.ChangeUses(-mov);
                localPlayerInventory.DropItem(new ItemInstance(item, Mathf.CeilToInt(mov / (float)item.MaxUses), (mov - 1) % item.MaxUses + 1));
            }
            return true;
        }
        public static bool TryRemove(Slot slot, string name, ref int amountToRemove)
        {
            if (slot.itemInstance.UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name2 = slot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name2 == null || name2 != name)
                return false;
            var item = Main.LookupItem(name);
            if (!item)
                return false;
            var total = slot.itemInstance.UsesInStack;
            var mov = Math.Min(amountToRemove * item.MaxUses, total);
            amountToRemove -= Mathf.CeilToInt(mov / (float)item.MaxUses);
            slot.ChangeUses(-mov);
            Main.ClearItemCache(name);
            return true;
        }

        public static bool TryRemoveUses(Slot slot, Inventory inventory, string name, ref int amountToRemove, bool addItemAfterUse)
        {
            if (slot.itemInstance.UniqueName != Main.compressedItem.UniqueName)
                return false;
            var name2 = slot.itemInstance.TryGetData(out var data) && data.TryGetValue(Main.storedKey, out var val) ? val : null;
            if (name2 == null || name2 != name)
                return false;
            Main.ClearItemCache(name);
            var total = slot.itemInstance.UsesInStack;
            var mov = Math.Min(amountToRemove, total);
            amountToRemove -= mov;
            slot.ChangeUses(-mov);
            if (addItemAfterUse)
            {
                var item = Main.LookupItem(name);
                if (item && item.settings_consumeable.ItemAfterUse?.item) {
                    var after = item.settings_consumeable.ItemAfterUse.item;
                    var lost = Mathf.CeilToInt(total / (float)item.MaxUses) - Mathf.CeilToInt((total - mov) / (float)item.MaxUses);
                    for (int i = 0; i < lost; i++)
                        inventory.AddItem(after.UniqueName, 1);
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Slot))]
    static class Patch_Slot
    {
        [HarmonyPatch("StackIsFull")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> StackIsFull_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            var ind = code.FindIndex(x => x.opcode == OpCodes.Ret) + 1;
            code[ind].labels.Add(target);
            code.InsertRange(ind, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(ForceStackNotFull))),
                new CodeInstruction(OpCodes.Brfalse,target),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Ret)
            });
            return code;
        }
        [HarmonyPatch("AddItem")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> AddItem_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var ind = code.FindLastIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_UniqueIndex"), x => x.operand is Label);
            var exit = (Label)code[ind].operand;
            var target = code[ind + 1].labels;
            code[ind + 1].labels = new List<Label>();
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0) { labels = target },
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(TryAddItem))),
                new CodeInstruction(OpCodes.Brtrue,exit)
            });
            return code;
        }
    }

    [HarmonyPatch(typeof(Inventory))]
    static class Patch_Inventory
    {
        [HarmonyPatch("MoveItem")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> MoveItem_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            code[code.FindLastIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "StackSlots"), x => x.operand is Label) + 1].labels.Add(target);
            var ind = code.FindLastIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_Stackable"), x => x.opcode == OpCodes.Ret) + 1;
            var jumps = code[ind].labels;
            code[ind].labels = new List<Label>();
            code.InsertRange(ind, new[]
            {
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"fromSlot")) {labels = jumps},
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"toSlot")),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(OverrideShouldTryStack))),
                new CodeInstruction(OpCodes.Brtrue,target)
            });
            var swtch = (code.Find(x => x.opcode == OpCodes.Switch).operand as IEnumerable<Label>).ToArray();
            foreach (var i in new[] { 0, 2 })
            {
                ind = code.FindIndex(code.FindIndex(x => x.labels.Contains(swtch[i])), x => x.operand is MethodInfo method && method.Name == "get_Amount") + 1;
                code.InsertRange(ind, new[] {
                    new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"fromSlot")),
                    new CodeInstruction(OpCodes.Ldc_I4,i),
                    new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(OverrideAmount)))
                });
            }
            ind = code.FindIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "IsPointerOverGameObject"), x => x.operand is Label);
            target = (Label)code[ind].operand;
            ind = code.FindLastIndex(code.FindIndex(ind, x => x.operand is Label l && l != target), x => x.operand is Label) + 1;
            code.InsertRange(ind, new[]
            {
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"fromSlot")),
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"localPlayerInventory")),
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"dragAmount")),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(CancelDrop))),
                new CodeInstruction(OpCodes.Brtrue,target)

            });
            return code;
        }
        

        [HarmonyPatch("StackSlots")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> StackSlots_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            code[code.FindLastIndex(code.FindLastIndex(x => x.operand is MethodInfo method && method.Name == "PlayUI_MoveItem"), x => x.labels.Count > 0)].labels.Add(target);
            code.InsertRange(0, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(OverrideTryStack))),
                new CodeInstruction(OpCodes.Brtrue,target)
            });
            return code;
        }

        [HarmonyPatch("MoveSlotToEmpty")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> MoveSlotToEmpty_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            code[code.FindLastIndex(x => x.operand is MethodInfo method && method.Name == "SetItem") + 1].labels.Add(target);
            code.InsertRange(0, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(OverrideTryMove))),
                new CodeInstruction(OpCodes.Brtrue,target)
            });
            return code;
        }

        [HarmonyPatch("FindSuitableSlot")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> FindSuitableSlot_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            code[code.FindLastIndex(x => x.opcode == OpCodes.Ldloc_3)].labels.Add(target);
            code.InsertRange(code.FindIndex(x => x.opcode == OpCodes.Stloc_3) + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(CanQuickStack))),
                new CodeInstruction(OpCodes.Brtrue,target)
            });
            return code;
        }

        [HarmonyPatch("HoverEnter")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> HoverEnter_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex(x => x.operand is MethodInfo method && method.Name == "SetItemDescription");
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"localPlayerInventory")),
                new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(Inventory),"hoverSlot")),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(OnHoverSlot)))
            });
            return code;
        }

        [HarmonyPatch("GetItemCount",typeof(Item_Base))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> GetItemCount_ItemBase_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            var jump = iL.DefineLabel();
            var newLocal = iL.DeclareLocal(typeof(int));
            code[code.FindLastIndex(x => x.opcode == OpCodes.Stloc_0) + 1].labels.Add(target);
            var ind = code.FindIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_IsEmpty"),x => x.operand is Label);
            code[ind + 1].labels.Add(jump);
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloca_S,newLocal),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(TryCountByItem))),
                new CodeInstruction(OpCodes.Brfalse_S,jump),
                new CodeInstruction(OpCodes.Ldloc_S,newLocal),
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Add),
                new CodeInstruction(OpCodes.Stloc_0),
                new CodeInstruction(OpCodes.Br_S,target)
            });
            return code;
        }

        [HarmonyPatch("GetItemCount", typeof(string))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> GetItemCount_String_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            var jump = iL.DefineLabel();
            var newLocal = iL.DeclareLocal(typeof(int));
            code[code.FindLastIndex(x => x.opcode == OpCodes.Stloc_0) + 1].labels.Add(target);
            var ind = code.FindIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_IsEmpty"), x => x.operand is Label);
            code[ind + 1].labels.Add(jump);
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloca_S,newLocal),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(TryCountByName))),
                new CodeInstruction(OpCodes.Brfalse_S,jump),
                new CodeInstruction(OpCodes.Ldloc_S,newLocal),
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Add),
                new CodeInstruction(OpCodes.Stloc_0),
                new CodeInstruction(OpCodes.Br_S,target)
            });
            return code;
        }

        [HarmonyPatch("RemoveItem")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> RemoveItem_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            var jump = iL.DefineLabel();
            var jump2 = iL.DefineLabel();
            var leave = code.Find(x => x.opcode == OpCodes.Leave_S);
            code[code.FindLastIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "MoveNext"), x => x.opcode == OpCodes.Ldloca_S && x.operand is LocalBuilder l && l.LocalIndex == 2 && Traverse.Create(l).Field("ilgen").GetValue() == null)].labels.Add(target);
            var ind = code.FindIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_IsEmpty"), x => x.operand is Label);
            code[ind + 1].labels.Add(jump);
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarga_S,2),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(TryRemove))),
                new CodeInstruction(OpCodes.Brfalse_S,jump),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Bge_S, jump2),
                new CodeInstruction(leave),
                new CodeInstruction(OpCodes.Br_S,target) { labels = new List<Label>() { jump2 } }
            });
            return code;
        }

        [HarmonyPatch("RemoveItemUses")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> RemoveItemUses_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            var jump = iL.DefineLabel();
            var jump2 = iL.DefineLabel();
            var leave = code.Find(x => x.opcode == OpCodes.Leave_S);
            code[code.FindLastIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "MoveNext"),x => x.opcode == OpCodes.Ldloca_S && x.operand is LocalBuilder l && l.LocalIndex == 2 && Traverse.Create(l).Field("ilgen").GetValue() == null)].labels.Add(target);
            var ind = code.FindIndex(code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_IsEmpty"), x => x.operand is Label);
            code[ind + 1].labels.Add(jump);
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarga_S,2),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(TryRemoveUses))),
                new CodeInstruction(OpCodes.Brfalse_S,jump),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Bge_S, jump2),
                new CodeInstruction(leave),
                new CodeInstruction(OpCodes.Br_S,target) { labels = new List<Label>() { jump2 } }
            });
            return code;
        }
    }
    [HarmonyPatch(typeof(PlayerInventory), "FindSuitableSlot")]
    static class Patch_FindSuitablePlayerSlot
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var target = iL.DefineLabel();
            code[code.FindLastIndex(x => x.opcode == OpCodes.Ldloc_3)].labels.Add(target);
            var ind = code.FindLastIndex(x => x.opcode == OpCodes.Ldloc_0);
            var labels = code[ind].labels;
            code[ind].labels = new List<Label>();
            code.InsertRange(ind, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_3) { labels = labels },
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(CanQuickStack))),
                new CodeInstruction(OpCodes.Brtrue,target)
            });
            return code;
        }
    }

    [HarmonyPatch(typeof(PickupItem), "GetPickupName")]
    static class Patch_PickupItemName
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_DisplayName");
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld,AccessTools.Field(typeof(PickupItem),"itemInstance")),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(FormatDisplayName)))
            });
            return code;
        }
    }

    [HarmonyPatch(typeof(ItemInstance))]
    static class Patch_ItemInstance
    {
        [HarmonyPatch("BaseItemMaxUses",MethodType.Getter)]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex(x => x.operand is MethodInfo method && method.Name == "get_MaxUses");
            if (code[ind].opcode == OpCodes.Call)
                code[ind].opcode = OpCodes.Callvirt;
            code.InsertRange(ind + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PatchMethods),nameof(OverrideInstanceMax)))
            });
            /*var s = "";
            var n = 0;
            foreach (var i in code)
                s +=
                    n++ + ": "
                    + i.opcode + (
                        i.operand == null
                            ? ""
                            : i.operand is Label l
                                ? " " + code.FindIndex(x => x.labels.Contains(l))
                                : (" " + i.operand.ToString()))
                    + "\n";
            Debug.Log(s);*/
            return code;
        }
    }

    [HarmonyPatch]
    static class Patch_ReplaceMaxUses
    {
        static MethodInfo replacement = AccessTools.Property(typeof(ItemInstance), "BaseItemMaxUses").GetGetMethod();
        static IEnumerable<MethodBase> TargetMethods()
        {
            var l = new List<MethodBase>();
            var a = typeof(Raft).Assembly;
            foreach (var t in a.GetTypes())
                foreach (var m in t.GetMethods(~BindingFlags.Default))
                    if (m != replacement)
                        try
                        {
                            var code = PatchProcessor.GetCurrentInstructions(m, out var iL);
                            for (int i = 1; i < code.Count; i++)
                                if (code[i].operand is MethodInfo method && method.Name == "get_MaxUses" && method.DeclaringType == typeof(Item_Base)
                                    && code[i - 1].operand is FieldInfo field && field.Name == "baseItem" && field.DeclaringType == typeof(ItemInstance))
                                {
                                    l.Add(m);
                                    break;
                                }
                        }
                    catch { }
            return l;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 1; i--)
                if (code[i].operand is MethodInfo method && method.Name == "get_MaxUses" && method.DeclaringType == typeof(Item_Base)
                    && code[i - 1].operand is FieldInfo field && field.Name == "baseItem" && field.DeclaringType == typeof(ItemInstance))
                {
                    code.RemoveAt(i);
                    code[i - 1] = new CodeInstruction(OpCodes.Callvirt, replacement);
                }
            return code;
        }
    }
}