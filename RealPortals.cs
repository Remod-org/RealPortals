#region License (GPL v2)
/*
    RealPortals
    Copyright (c) 2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; version 2
    of the License only.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("RealPortals", "RFC1920", "1.0.4")]
    [Description("Define and manage portals using a tool gun, with permission of course.")]
    internal class RealPortals : RustPlugin
    {
        #region vars
        [PluginReference]
        private readonly Plugin Friends, Clans, Backpacks, GridAPI;

        private ConfigData configData;
        private const string permUse = "realportals.use";
        private const string permGun = "realportals.gun";
        private Dictionary<int, PortalPair> portals = new Dictionary<int, PortalPair>();
        private Dictionary<ulong, uint> issuedTools = new Dictionary<ulong, uint>();
        const string PortalPrefab = "assets/prefabs/missions/portal/bunker_door_portal.prefab";
        private bool newsave;
        public class PortalPair
        {
            public int id;
            public string name;
            public ulong ownerid;
            public bool IsServer;
            public bool Bidirectional = true;
            public bool Friends;
            public NetworkableId Entrance;
            public NetworkableId Exit;
            public Vector3 enloc;
            public Quaternion enrot;
            public Vector3 exloc;
            public Quaternion exrot;
        }
        #endregion vars

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region hooks
        private void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permGun, this);
            AddCovalenceCommand("portal", "cmdPortal");
            LoadData();
            LoadConfigVariables();
            if (newsave)
            {
                portals = new Dictionary<int, PortalPair>();
                SaveData();
                newsave = false;
                return;
            }
            RespawnPortals();
        }
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["addedto"] = "A portal gun has been added to {0}",
                ["alreadyin"] = "You already have a portal gun in {0}",
                ["issued"] = "You have already been issued a portal gun today",
                ["backpack"] = "your backpack",
                ["belt"] = "your belt",
                ["main"] = "your main inventory",
                ["noroom"] = "You have no room to store a portal gun",
                ["tooquick"] = "You must wait {0} seconds between radar bursts.",
                ["notafriend"] = "You must be friends with the owner of this portal!",
                ["notaffiliated"] = "You are not affiliated with the owner of this portal!",
                ["exitportal"] = "Exit portal {0}",
                ["entranceportal"] = "Entrance portal {0}",
                ["exitonly"] = "This is an exit only portal door!",
                ["private"] = "This is a private portal!",
                ["portaldescr"] = "Portal {0}\n Name: {1}\n Public: {2}\n BiDirectional: {3}\n Owner: {4}\n Entrance: {5} {6}\n Exit: {7} {8}\n",
                ["notauthorized"] = "You don't have permission to do that !!"
            }, this);
        }

        private void OnNewSave() => newsave = true;

        private void Unload()
        {
            // Only for dev use
            foreach (PortalPair pair in portals.Values)
            {
                BasePortal entrance = BaseNetworkable.serverEntities.Find(pair.Entrance) as BasePortal;
                BasePortal exit = BaseNetworkable.serverEntities.Find(pair.Exit) as BasePortal;
                entrance?.Kill();
                exit?.Kill();
            }
        }

        private void OnPlayerConnected(BasePlayer player) => OnPlayerDisconnected(player);
        private void OnPlayerDisconnected(BasePlayer player)
        {
            // Destroy toolgun on connect/disconnect
            Item foundInBelt = player.inventory.containerBelt.FindItemsByItemID(1803831286).FirstOrDefault();
            foundInBelt?.GetHeldEntity()?.Kill();
            foundInBelt?.DoRemove();
            player.inventory.containerBelt.MarkDirty();

            Item foundInMain = player.inventory.containerMain.FindItemsByItemID(1803831286).FirstOrDefault();
            foundInMain?.GetHeldEntity()?.Kill();
            foundInMain?.DoRemove();
            player.inventory.containerMain.MarkDirty();

            Item foundInBackpack = null;
            if (Backpacks)
            {
                ItemContainer backpack = Backpacks?.Call("API_GetBackpackContainer", player.userID) as ItemContainer;
                if (backpack != null)
                {
                    for (int i = 0; i < backpack.itemList.Count; i++)
                    {
                        if (backpack.itemList[i].info.itemid == 1803831286)
                        {
                            foundInBackpack = backpack.itemList[i];
                        }
                    }
                    foundInBackpack?.GetHeldEntity()?.Kill();
                    foundInBackpack?.DoRemove();
                    backpack.MarkDirty();

                }
            }
            // Let them create a new one when they rejoin before plugin reload.
            issuedTools.Remove(player.userID);
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null) return;
            if (input == null) return;
            //if (input.current.buttons > 0) Puts($"OnPlayerInput: {input.current.buttons}");

            if (!permission.UserHasPermission(player.UserIDString, permGun))
            {
                // User lacks permGun permission.
                return;
            }

            if (player?.GetHeldEntity() is Toolgun)
            {
                if (input.IsDown(BUTTON.USE))
                {
                    if (input.WasJustPressed(BUTTON.FIRE_PRIMARY))
                    {
                        SpawnPortal(player);
                    }
                    else if (input.WasJustPressed(BUTTON.FIRE_SECONDARY))
                    {
                        // Remove one of our portal pairs
                        BaseEntity target = RaycastAll<BaseEntity>(player.eyes.HeadRay()) as BaseEntity;
                        if (target != null && target is BasePortal)
                        {
                            CleanupPortal(target as BasePortal, player);
                        }
                    }
                }
                else if (input.WasJustPressed(BUTTON.FIRE_PRIMARY))
                {
                    BaseEntity target = RaycastAll<BaseEntity>(player.eyes.HeadRay()) as BaseEntity;
                    if (target != null && target is BasePortal)
                    {
                        // Found a portal
                        KeyValuePair<int, PortalPair> query = portals.FirstOrDefault(x => x.Value.Entrance == target.net.ID);
                        if (query.Key > 0)
                        {
                            Message(player.IPlayer, "entranceportal", query.Value.name);
                            return;
                        }
                        query = portals.FirstOrDefault(x => x.Value.Exit == target.net.ID);
                        if (query.Key > 0)
                        {
                            Message(player.IPlayer, "exitportal", query.Value.name);
                            return;
                        }
                        // Not one of ours...
                    }
                }
            }
        }

        private object OnPortalUse(BasePlayer player, BasePortal use)
        {
            KeyValuePair<int, PortalPair> target = portals.FirstOrDefault(x => x.Value.Entrance == use.net.ID || x.Value.Exit == use.net.ID);
            if (target.Key > 0 && use.targetID.Value > 0)
            {
                if (configData.requirePermission && !permission.UserHasPermission(player.UserIDString, permUse))
                {
                    // User lacks permUse permission
                    return true;
                }
                if (use.net.ID == target.Value.Exit && !target.Value.Bidirectional)
                {
                    // One-way portal - !BiDirectional
                    Message(player.IPlayer, "exitonly");
                    return true;
                }
                if (!target.Value.IsServer && !target.Value.Friends && target.Value.ownerid != player.userID)
                {
                    // Not server-shared, and friends are not allowed.
                    Message(player.IPlayer, "private");
                    return true;
                }
                if (!target.Value.IsServer && target.Value.Friends && !IsFriend(player.userID, target.Value.ownerid))
                {
                    // Not server-shared, and friend check failed.  Too bad, so sad.
                    Message(player.IPlayer, "notafriend");
                    return true;
                }
                // Play effect if this is one of our portals, but only if entrance and exit have already been defined.
                Effect.server.Run("assets/prefabs/npc/sam_site_turret/effects/tube_launch.prefab", player.transform.position, Vector3.up, null, false);
            }
            return null;
        }
        #endregion hooks

        #region commands
        private void cmdPortal(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;

            Puts(string.Join(",", args));
            if (args.Length == 1 && args[0] == "tool")
            {
                if (!permission.UserHasPermission(iplayer.Id, permGun)) return;

                // Issue a toolgun
                ItemContainer backpack = null;
                if (player.inventory.containerBelt.FindItemsByItemID(1803831286).FirstOrDefault() != null)
                {
                    Message(iplayer, "alreadyin", "belt");
                    return;
                }
                if (player.inventory.containerMain.FindItemsByItemID(1803831286).FirstOrDefault() != null)
                {
                    Message(iplayer, "alreadyin", "main");
                    return;
                }
                if (Backpacks)
                {
                    backpack = Backpacks?.Call("API_GetBackpackContainer", ulong.Parse(iplayer.Id)) as ItemContainer;
                    if (backpack != null)
                    {
                        for (int i = 0; i < backpack.itemList.Count; i++)
                        {
                            if (backpack.itemList[i].info.itemid == 1803831286)
                            {
                                Message(iplayer, "alreadyin", "backpack");
                                return;
                            }
                        }
                    }
                }

                if (!player.inventory.containerBelt.IsFull())
                {
                    Item item = ItemManager.CreateByItemID(1803831286, 1, 0);
                    item.MoveToContainer(player.inventory.containerBelt);
                    issuedTools.Add(player.userID, (uint)item.uid.Value);
                    player.inventory.containerBelt.MarkDirty();
                    Message(iplayer, "addedto", "belt");
                    return;
                }
                if (!player.inventory.containerMain.IsFull())
                {
                    Item item = ItemManager.CreateByItemID(1803831286, 1, 0);
                    item.MoveToContainer(player.inventory.containerMain);
                    issuedTools.Add(player.userID, (uint)item.uid.Value);
                    player.inventory.containerMain.MarkDirty();
                    Message(iplayer, "addedto", "main");
                    return;
                }
                if (backpack?.IsFull() == false)
                {
                    Item item = ItemManager.CreateByItemID(1803831286, 1, 0);
                    item.MoveToContainer(backpack);
                    issuedTools.Add(player.userID, (uint)item.uid.Value);
                    backpack.MarkDirty();
                    Message(iplayer, "addedto", "backpack");
                    return;
                }
                Message(iplayer, "noroom");
                return;
            }

            BaseEntity target = RaycastAll<BaseEntity>((iplayer.Object as BasePlayer).eyes.HeadRay()) as BaseEntity;
            KeyValuePair<int, PortalPair> query = new KeyValuePair<int, PortalPair>();
            if (target != null && target is BasePortal)
            {
                query = portals.FirstOrDefault(x => x.Value.Entrance == target.net.ID || x.Value.Exit == target.net.ID);
            }

            if (query.Key > 0)
            {
                if (args.Length == 2)
                {
                    switch (args[0])
                    {
                        case "swap":
                            NetworkableId newexit = query.Value.Entrance;
                            Vector3 newexitv = query.Value.enloc;
                            Quaternion newexitr = query.Value.enrot;

                            query.Value.Exit = query.Value.Entrance;
                            query.Value.exloc = query.Value.enloc;
                            query.Value.exrot = query.Value.enrot;

                            query.Value.Entrance = newexit;
                            query.Value.enloc = newexitv;
                            query.Value.enrot = newexitr;
                            break;
                        case "name":
                            query.Value.name = args[1];
                            break;
                        case "all":
                        case "public":
                            query.Value.IsServer = GetBoolValue(args[1]);
                            break;
                        case "friends":
                        case "team":
                            query.Value.Friends = GetBoolValue(args[1]);
                            break;
                        case "bi":
                        case "twoway":
                            query.Value.Bidirectional = GetBoolValue(args[1]);
                            break;
                    }
                    SaveData();
                }

                BaseNetworkable A = BaseNetworkable.serverEntities.Find(query.Value.Entrance);
                BaseNetworkable B = BaseNetworkable.serverEntities.Find(query.Value.Exit);
                if (A != null && B != null)
                {
                    string owner = BasePlayer.Find(query.Value.ownerid.ToString())?.displayName;
                    Message(iplayer, "portaldescr",
                        query.Key.ToString(), query.Value.name, query.Value.IsServer.ToString(),
                        query.Value.Bidirectional.ToString(), owner,
                        PositionToGrid(A.transform.position), A.transform.position,
                        PositionToGrid(B.transform.position), B.transform.position
                    );
                }
            }
        }
        #endregion commands

        #region functions
        private void CleanupPortal(BasePortal EntOrExit, BasePlayer player)
        {
            // Remove portal definition as well as both entrance and exit objects, regardless of which of the pair was sent.
            KeyValuePair<int, PortalPair> target = portals.FirstOrDefault(x => x.Value.Entrance == EntOrExit.net.ID || x.Value.Exit == EntOrExit.net.ID);
            if (target.Key > 0)
            {
                if (!player.IsAdmin && !IsFriend(player.userID, target.Value.ownerid))
                {
                    // Only allow admin, owner, or a friend to remove this portal.
                    Message(player.IPlayer, "notaffiliated");
                    return;
                }

                Puts("Found entrance for portal - removing all");
                BaseNetworkable entrance = BaseNetworkable.serverEntities.Find(target.Value.Entrance);
                BaseNetworkable exit = BaseNetworkable.serverEntities.Find(target.Value.Exit);
                portals.Remove(target.Key);
                SaveData();
                entrance?.Kill();
                exit?.Kill();
            }
        }

        private void RespawnPortals()
        {
            foreach (KeyValuePair<int, PortalPair> portalPair in new Dictionary<int, PortalPair>(portals))
            {
                BaseEntity entrance = GameManager.server.CreateEntity(PortalPrefab, portalPair.Value.enloc, portalPair.Value.enrot, true);
                entrance.Spawn();
                portals[portalPair.Key].Entrance = entrance.net.ID;

                BaseEntity exit = GameManager.server.CreateEntity(PortalPrefab, portalPair.Value.exloc, portalPair.Value.exrot, true);
                exit.Spawn();
                portals[portalPair.Key].Exit = exit.net.ID;

                (exit as BasePortal).targetPortal = entrance as BasePortal;
                (exit as BasePortal).transitionSoundEffect = new GameObjectRef();
                (entrance as BasePortal).targetPortal = exit as BasePortal;
                (entrance as BasePortal).transitionSoundEffect = new GameObjectRef();
            }
            SaveData();
        }

        private void SpawnPortal(BasePlayer player)
        {
            Vector3 pos = new Vector3(player.transform.position.x, player.transform.position.y + 0.5f, player.transform.position.z);
            Quaternion rot = player.transform.rotation;

            int currentId = UnityEngine.Random.Range(0, 2147483647);
            KeyValuePair<int, PortalPair> current = portals.FirstOrDefault(x => x.Value.Exit.Value == 0);
            bool isExit = false;
            if (current.Key > 0)
            {
                // Spawning Exit for existing portal
                currentId = current.Key;
                isExit = true;
            }
            else
            {
                // Spawning new portal Entrance
                portals.Add(currentId, new PortalPair()
                {
                    id = currentId,
                    ownerid = player.userID,
                    name = "NewPortal",
                    Entrance = new NetworkableId(),
                    Exit = new NetworkableId()
                });
                current = portals.FirstOrDefault(x => x.Value.id == currentId);
                isExit = false;
            }
            BaseEntity newPortal = GameManager.server.CreateEntity(PortalPrefab, pos, rot, true);
            //newPortal.name = "NewPortal";
            newPortal.Spawn();

            BasePortal p = newPortal as BasePortal;
            //p.appearEffect = new GameObjectRef();
            //p.disappearEffect = new GameObjectRef();
            p.transitionSoundEffect = new GameObjectRef();

            switch (isExit)
            {
                case false:
                    current.Value.Entrance = newPortal.net.ID;
                    current.Value.enloc = newPortal.transform.position;
                    current.Value.enrot = newPortal.transform.rotation;
                    break;
                case true:
                    current.Value.Exit = newPortal.net.ID;
                    current.Value.exloc = newPortal.transform.position;
                    current.Value.exrot = newPortal.transform.rotation;
                    BasePortal entrance = BaseNetworkable.serverEntities.Find(current.Value.Entrance) as BasePortal;
                    BasePortal exit = BaseNetworkable.serverEntities.Find(current.Value.Exit) as BasePortal;
                    exit.targetPortal = entrance;
                    entrance.targetPortal = exit;
                    break;
            }
            SaveData();
        }
        #endregion functions

        #region data
        private void LoadData()
        {
            portals = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, PortalPair>>(Name + "/portals");
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/portals", portals);
        }
        #endregion data

        #region config
        private class ConfigData
        {
            public bool requirePermission;
            public bool useFriends;
            public bool useClans;
            public bool useTeams;

            public VersionNumber Version;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData()
            {
                requirePermission = true,
                useClans = true,
                useTeams = true,
                useFriends = true
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }
        #endregion config

        #region Utils
        private static bool GetBoolValue(string value)
        {
            if (value == null)
            {
                return false;
            }
                // TODO:

            value = value.Trim().ToLower();
            switch (value)
            {
                case "on":
                case "true":
                case "yes":
                case "1":
                case "t":
                case "y":
                    return true;
                default:
                    return false;
            }
        }

        private object RaycastAll<T>(Ray ray) where T : BaseEntity
        {
            RaycastHit[] hits = Physics.RaycastAll(ray);
            GamePhysics.Sort(hits);
            const float distance = 100f;
            object target = false;
            foreach (RaycastHit hit in hits)
            {
                BaseEntity ent = hit.GetEntity();
                if (ent is T && hit.distance < distance)
                {
                    target = ent;
                    break;
                }
            }

            return target;
        }

        public string PositionToGrid(Vector3 position)
        {
            if (GridAPI != null)
            {
                string[] g = (string[]) GridAPI.CallHook("GetGrid", position);
                return string.Concat(g);
            }
            else
            {
                // From GrTeleport for display only
                Vector2 r = new Vector2((World.Size / 2) + position.x, (World.Size / 2) + position.z);
                float x = Mathf.Floor(r.x / 146.3f) % 26;
                float z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        // playerid = requesting player, ownerid = target or owner of a home
        private bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (playerid == ownerid) return true;
            if (configData.useFriends && Friends != null)
            {
                object fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if (configData.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if (configData.useTeams)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if (player.currentTeam != 0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                    if (playerTeam == null) return false;
                    if (playerTeam.members.Contains(ownerid))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion Utils
    }
}
