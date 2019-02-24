using StardewModdingAPI;
using StardewValley;
using StardewModdingAPI.Events;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Polygamy
{
    public class Mod : StardewModdingAPI.Mod
    {
#if DEBUG
        private static readonly bool DEBUG = true;
#else
        private static readonly bool DEBUG = false;
#endif
        public static Mod Instance;
        public static bwdyworks.ModUtil ModUtil;

        internal List<NPC> MarryableNPCs = new List<NPC>();
        internal List<NPC> DatableNPCs = new List<NPC>();
        public PolyData PolyData;

        public override void Entry(IModHelper helper)
        {
            ModUtil = new bwdyworks.ModUtil(this);
            Instance = this;
            if(ModUtil.StartConfig(DEBUG))
            {
                helper.Events.Input.ButtonPressed += Input_ButtonPressed;
                helper.Events.GameLoop.DayStarted += GameLoop_DayStarted;
                helper.Events.GameLoop.Saving += GameLoop_Saving;
                helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
                helper.Events.GameLoop.TimeChanged += GameLoop_TimeChanged;
                ModUtil.EndConfig();
            }
        }

        private void GameLoop_SaveLoaded(object sender, SaveLoadedEventArgs e)
        {

            PolyData = Mod.Instance.Helper.Data.ReadJsonFile<PolyData>("Saves/polySpouse." + Constants.SaveFolderName + ".json");
            if (PolyData == null)
            {
                PolyData = new PolyData();
            }
            else Mod.Instance.Monitor.Log("Polygamy loaded.");
        }

        private void GameLoop_Saving(object sender, SavingEventArgs e)
        {
            Mod.Instance.Monitor.Log("Polygamy saved.");
            Mod.Instance.Helper.Data.WriteJsonFile("Saves/polySpouse." + Constants.SaveFolderName + ".json", PolyData);
        }

        private void GameLoop_DayStarted(object sender, DayStartedEventArgs e)
        {
            //prep data
            if (!PolyData.PolyDates.ContainsKey(Game1.player.UniqueMultiplayerID))
            {
                PolyData.PolyDates[Game1.player.UniqueMultiplayerID] = new List<string>();
            }
            if (!PolyData.PolySpouses.ContainsKey(Game1.player.UniqueMultiplayerID))
            {
                PolyData.PolySpouses[Game1.player.UniqueMultiplayerID] = new List<string>();
            }
            //find all the marriage candidates
            MarryableNPCs.Clear();
            Dictionary<string, string> dictionary = Game1.content.Load<Dictionary<string, string>>("Data\\NPCDispositions");
            var candidates = new List<string>();
            foreach(string s in dictionary.Keys)
            {
                var c = Game1.getCharacterFromName(s);
                if(c != null && c.datable.Value) candidates.Add(s);
            }
            //find dateable
            foreach (string name in candidates)
            {
                var n = Game1.getCharacterFromName(name, true);
                if (n == null) continue; //character renamed or removed by mod
                bool taken = false;
                //belongs directly to a player?
                foreach (var f in Game1.getAllFarmers()) if (f.spouse == n.Name) taken = true;
                //belongs to someone as a poly spouse
                foreach (var ps in PolyData.PolySpouses) if (ps.Value.Contains(n.Name)) taken = true;
                //already belongs to you as a poly date (we don't care if someone else is only dating them)
                foreach (var ps2 in PolyData.PolyDates[Game1.player.UniqueMultiplayerID]) if (ps2.Contains(n.Name)) taken = true;
                if (!taken) DatableNPCs.Add(n);
            }
            Monitor.Log("Found " + DatableNPCs.Count + " dateable NPCs.", LogLevel.Info);
            //find marryable
            foreach (string name in candidates)
            {
                var n = Game1.getCharacterFromName(name, true);
                if (n == null) continue; //character renamed or removed by mod
                bool taken = false;
                //belongs directly to a player?
                foreach (var f in Game1.getAllFarmers()) if (f.spouse == n.Name) taken = true;
                //belongs to someone as a poly spouse
                foreach (var ps in PolyData.PolySpouses) if (ps.Value.Contains(n.Name)) taken = true;
                //must already be dating or polydating you
                bool dating = false;
                foreach (var ps2 in PolyData.PolyDates[Game1.player.UniqueMultiplayerID]) if (ps2.Contains(n.Name)) dating = true;
                if (Game1.player.friendshipData.ContainsKey(n.Name))
                {
                    if (Game1.player.friendshipData[n.Name].Status == FriendshipStatus.Dating) dating = true;
                }
                if (!taken && dating) MarryableNPCs.Add(n);
            }
            Monitor.Log("Found " + MarryableNPCs.Count + " marryable NPCs.", LogLevel.Info);

            //pick a random spouse for me today
            if (PolyData.PolySpouses.ContainsKey(Game1.player.UniqueMultiplayerID) && PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Count > 0)
            {
                List<string> spouseNames = new List<string>(PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].ToArray());
                //check for birthday spouse
                bool birthday = false;
                string actualSpouseForToday = null;
                for (int i = 0; i < spouseNames.Count; i++)
                {
                    var testBday = Game1.getCharacterFromName(spouseNames[i], true);
                    if (testBday.isBirthday(Game1.currentSeason, Game1.dayOfMonth))
                    {
                        birthday = true;
                        actualSpouseForToday = spouseNames[i];
                    }
                }
                if (!birthday)
                {
                    //pick a random
                    actualSpouseForToday = spouseNames[ModUtil.RNG.Next(spouseNames.Count)];
                }
                //put yesterday's spouse in the bed
                var actualSpouseYesterday = Game1.player.spouse;
                if (actualSpouseYesterday != null)
                {
                    var bedSpawn = (Game1.getLocationFromName(Game1.player.homeLocation.Value) as StardewValley.Locations.FarmHouse).getSpouseBedSpot();
                    ModUtil.WarpNPC(Game1.getCharacterFromName(actualSpouseYesterday), Game1.player.homeLocation.Value, bedSpawn);
                    FixSpouseSchedule(Game1.getLocationFromName(Game1.player.homeLocation.Value), Game1.getCharacterFromName(actualSpouseYesterday), true);
                }
                //take new spouse out of poly spouses
                if (actualSpouseForToday != null)
                {
                    Monitor.Log("'Actual' spouse today: " + actualSpouseForToday);
                    PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Remove(actualSpouseForToday);
                    Game1.player.spouse = actualSpouseForToday;
                    PolyData.PrimarySpouse = actualSpouseForToday;
                    Utility.getHomeOfFarmer(Game1.player).showSpouseRoom();
                    Game1.getFarm().addSpouseOutdoorArea(Game1.player.spouse);
                    NPC actualSpouse = Game1.getCharacterFromName(actualSpouseForToday);
                    var kitchenSpawn = (Game1.getLocationFromName(Game1.player.homeLocation.Value) as StardewValley.Locations.FarmHouse).getKitchenStandingSpot();
                    ModUtil.WarpNPC(actualSpouse, Game1.player.homeLocation.Value, kitchenSpawn);
                    FixSpouseSchedule(Game1.getLocationFromName(Game1.player.homeLocation.Value), actualSpouse);
                }
                //distribute remaining spouses
                foreach (var otherSpouseName in PolyData.PolySpouses[Game1.player.UniqueMultiplayerID])
                {
                    NPC otherSpouse = Game1.getCharacterFromName(otherSpouseName);
                    //find a free tile to position them on
                    GameLocation l = Game1.player.currentLocation;
                    var p = FindSpotForNPC(l, l is StardewValley.Locations.FarmHouse, otherSpouse.getTileLocationPoint());
                    if(p != Point.Zero)
                    {
                        ModUtil.WarpNPC(otherSpouse, l, p);
                    }
                    //and fix their schedule
                    FixSpouseSchedule(l, otherSpouse, true);
                }
                //add yesterday's spouse back to poly spouses
                if (actualSpouseYesterday != null) PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Add(actualSpouseYesterday);
            }
        }

        public void FixSpouseSchedule(GameLocation l, NPC npc, bool poly = false)
        {
            if (poly)
            {
                npc.DefaultPosition = new Vector2(npc.getTileX() * 64, npc.getTileY() * 64);
                npc.DefaultMap = npc.currentLocation.Name;
                if (ModUtil.RNG.Next(2) == 1)
                {
                    var p = FindSpotForNPC(l, l is StardewValley.Locations.FarmHouse, npc.getTileLocationPoint());
                    npc.controller = new PathFindController(npc, l, new Point(p.X, p.Y), ModUtil.RNG.Next(4));
                }
            } else
            {
                string text = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                if ((npc.Name.Equals("Penny") && (text.Equals("Tue") || text.Equals("Wed") || text.Equals("Fri"))) || (npc.Name.Equals("Maru") && (text.Equals("Tue") || text.Equals("Thu"))) || (npc.Name.Equals("Harvey") && (text.Equals("Tue") || text.Equals("Thu"))))
                {
                    npc.setNewDialogue("MarriageDialogue", "jobLeave_", -1, add: false, clearOnMovement: true);
                }
                if (!Game1.isRaining)
                {
                    npc.setNewDialogue("MarriageDialogue", "funLeave_", -1, add: false, clearOnMovement: true);
                }
                npc.followSchedule = false;
                npc.endOfRouteMessage.Value = null;
                if (!Game1.player.divorceTonight.Value) npc.marriageDuties();
            }
        }


        private void GameLoop_TimeChanged(object sender, TimeChangedEventArgs e)
        {
            //update poly spouses, don't leave them stagnant
            foreach (var spouse in PolyData.PolySpouses[Game1.player.UniqueMultiplayerID])
            {
                if (ModUtil.RNG.Next(7) == 1)
                {
                    NPC spouseNpc = Game1.getCharacterFromName(spouse);
                    GameLocation l = spouseNpc.currentLocation;
                    bool warped = false;
                    if(l.farmers.Count == 0) //noone's looking. we could move them to an adjacent map.
                    {
                        if (ModUtil.RNG.Next(5) == 0)
                        {
                            Warp w = l.warps[ModUtil.RNG.Next(l.warps.Count)];
                            GameLocation l2 = Game1.getLocationFromName(w.TargetName);
                            if (l2.farmers.Count == 0) //but only if we're not looking here either. have to skip the NPCBarriers.
                            {
                                l.characters.Remove(spouseNpc);
                                l2.addCharacterAtRandomLocation(spouseNpc);
                                Monitor.Log(spouseNpc.Name + " moved to " + l2.Name);
                                l = spouseNpc.currentLocation;
                                warped = true;
                            }
                        }
                    }
                    var p = FindSpotForNPC(l, l is StardewValley.Locations.FarmHouse, spouseNpc.getTileLocationPoint(), warped ? 8 : 0);
                    if (p != Point.Zero)
                    {
                        spouseNpc.willDestroyObjectsUnderfoot = false;
                        spouseNpc.controller = new PathFindController(spouseNpc, l, new Point((int)p.X, (int)p.Y), -1, OnSpouseWalkComplete, 100);
                    }
                }
            }
        }

        public void OnSpouseWalkComplete(Character c, GameLocation l)
        {
            //c.controller = null;
        }

        public List<Rectangle> GetVanillaHouseWallRects()
        {
            List<Rectangle> list = new List<Rectangle>();
            switch (Game1.player.HouseUpgradeLevel)
            {
                case 0:
                    list.Add(new Rectangle(1, 1, 10, 3));
                    break;
                case 1:
                    list.Add(new Rectangle(1, 1, 17, 3));
                    list.Add(new Rectangle(18, 6, 2, 2));
                    list.Add(new Rectangle(20, 1, 9, 3));
                    break;
                case 2:
                case 3:
                    list.Add(new Rectangle(1, 1, 12, 3));
                    list.Add(new Rectangle(15, 1, 13, 3));
                    list.Add(new Rectangle(13, 3, 2, 2));
                    list.Add(new Rectangle(1, 10, 10, 3));
                    list.Add(new Rectangle(13, 10, 8, 3));
                    list.Add(new Rectangle(21, 15, 2, 2));
                    list.Add(new Rectangle(23, 10, 11, 3));
                    break;
            }
            return list;
        }

        public Point FindSpotForNPC(GameLocation l, bool checkVanillaHouseWalls, Point p, int radius = 0)
        {
            Point randomPoint = Point.Zero;
            for (int i = 0; i < 100; i++)
            {
                int sizeX = l.map.GetLayer("Back").TileWidth;
                int sizeY = l.map.GetLayer("Back").TileHeight;
                if(radius > 0)
                    randomPoint = new Point((p.X - radius) + ModUtil.RNG.Next(radius * 2), (p.Y - radius) + ModUtil.RNG.Next(radius * 2));
                else
                    randomPoint = new Point(ModUtil.RNG.Next(sizeX), ModUtil.RNG.Next(sizeY));
                bool unacceptable = false;
                unacceptable = (l.getTileIndexAt(randomPoint.X, randomPoint.Y, "Back") == -1 || !l.isTileLocationTotallyClearAndPlaceable(randomPoint.X, randomPoint.Y) || (checkVanillaHouseWalls && Utility.pointInRectangles(GetVanillaHouseWallRects(), randomPoint.X, randomPoint.Y)));
                if (!unacceptable) return randomPoint;
            }
            return Point.Zero;
        }

        private void Input_ButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button.IsActionButton())
            {
                if (Context.IsPlayerFree)
                {
                    //to date
                    //holding the bouquet?
                    if (Game1.player.ActiveObject != null && Game1.player.ActiveObject.ParentSheetIndex == 458)
                    {
                        var target = ModUtil.GetLocalPlayerFacingTileCoordinate();
                        var key = Game1.currentLocation.Name + "." + target[0] + "." + target[1];
                        //check if npc is in front of player
                        NPC tnpc = null;
                        foreach (NPC n in DatableNPCs)
                        {
                            if (n.getTileX() == target[0] && n.getTileY() == target[1] && n.currentLocation != null && n.currentLocation.Name == Game1.currentLocation.Name)
                            {
                                tnpc = n;
                                break;
                            }
                        }
                        if (tnpc == null)
                        {
                            return;
                        }
                        if (ModUtil.GetFriendshipPoints(tnpc.Name) >= 2000) //ready for relationship!
                        {
                            Helper.Input.Suppress(e.Button);
                            tnpc.faceTowardFarmerForPeriod(5000, 60, false, Game1.player);

                            PolyData.PolyDates[Game1.player.UniqueMultiplayerID].Add(tnpc.Name);
                            ModUtil.SetFriendshipPoints(tnpc.Name, 2000);
                            Game1.player.friendshipData[tnpc.Name].Status = FriendshipStatus.Dating;
                            DatableNPCs.Remove(tnpc);
                            MarryableNPCs.Add(tnpc);

                            tnpc.CurrentDialogue.Push(new Dialogue((Game1.random.NextDouble() < 0.5) ? Game1.LoadStringByGender(tnpc.Gender, "Strings\\StringsFromCSFiles:NPC.cs.3962") : Game1.LoadStringByGender(tnpc.Gender, "Strings\\StringsFromCSFiles:NPC.cs.3963"), tnpc));
                            Game1.player.reduceActiveItemByOne();
                            Game1.player.completelyStopAnimatingOrDoingAction();
                            tnpc.doEmote(20);
                            Game1.drawDialogue(tnpc);
                        }
                    }

                    //to marry
                    //dating?
                    //holding the mermaid's pendant?
                    else if (Game1.player.ActiveObject != null && Game1.player.ActiveObject.ParentSheetIndex == 460)
                    {
                        if (Game1.player.spouse != null) //we only need polygamy for second+ spouse
                        {
                            var target = ModUtil.GetLocalPlayerFacingTileCoordinate();
                            var key = Game1.currentLocation.Name + "." + target[0] + "." + target[1];
                            //check if npc is in front of player
                            NPC tnpc = null;
                            foreach (NPC n in MarryableNPCs)
                            {
                                if (n.getTileX() == target[0] && n.getTileY() == target[1] && n.currentLocation != null && n.currentLocation.Name == Game1.currentLocation.Name)
                                {
                                    tnpc = n;
                                    break;
                                }
                            }
                            if (tnpc == null || tnpc.Name == Game1.player.spouse) return;
                            if (ModUtil.GetFriendshipPoints(tnpc.Name) >= 2500) //ready for marriage!
                            {
                                Helper.Input.Suppress(e.Button);
                                //if so we can override the dialogue here if conditions are met
                                Game1.changeMusicTrack("none");
                                //demote current spouse to side piece\
                                if (Game1.player.HouseUpgradeLevel < 3) Game1.player.HouseUpgradeLevel = 3; //prevent crash
                                if (Game1.player.spouse != null)
                                {
                                    //push the spouse into a poly slot
                                    PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Add(Game1.player.spouse);
                                }
                                //marry the new one while they're still interesting
                                //WITH wedding ceremony (as opposed to hotswapping without)
                                ModUtil.SetFriendshipPoints(tnpc.Name, 2500);
                                Game1.player.spouse = tnpc.Name;
                                PolyData.PrimarySpouse = tnpc.Name;
                                tnpc.CurrentDialogue.Clear();
                                tnpc.CurrentDialogue.Push(new Dialogue(Game1.content.Load<Dictionary<string, string>>("Data\\EngagementDialogue")[tnpc.Name + "0"], tnpc));
                                tnpc.CurrentDialogue.Push(new Dialogue(Game1.content.Load<Dictionary<string, string>>("Data\\EngagementDialogue")[tnpc.Name + "1"], tnpc));
                                //tnpc.CurrentDialogue.Push(new Dialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:NPC.cs.3980"), tnpc));
                                Game1.player.reduceActiveItemByOne();
                                Game1.player.completelyStopAnimatingOrDoingAction();
                                Game1.drawDialogue(tnpc);
                                //DO WEDDIN' NAO!
                                Game1.player.friendshipData[Game1.player.spouse].WeddingDate = null;
                                Game1.weddingToday = true;
                                Game1.player.friendshipData[Game1.player.spouse].Status = FriendshipStatus.Engaged;
                                Game1.checkForWedding();
                                Game1.player.friendshipData[Game1.player.spouse].Status = FriendshipStatus.Married;
                            }
                        }
                    }
                }
            }
        }
    }
}