using StardewModdingAPI;
using StardewValley;
using StardewModdingAPI.Events;
using System.Collections.Generic;
using System;
using Microsoft.Xna.Framework;

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

        internal List<NPC> NPCs = new List<NPC>();
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
            //find all the marriage candidates
            NPCs.Clear();
            var candidates = new[]
            {
                "Alex", "Elliott", "Harvey", "Sam", "Sebastian", "Shane",
                "Abigail", "Emily", "Haley", "Leah", "Maru", "Penny"
            };
            foreach (string name in candidates)
            {
                var n = Game1.getCharacterFromName(name, true);
                if (n == null) continue; //character renamed or removed by mod
                bool taken = false;
                if (n.Name == Game1.player.spouse)
                {
                    taken = true; //uh, derp?
                    //Monitor.Log("Removing primary spouse: " + Game1.player.spouse);
                }
                else
                {
                    foreach (var ps in PolyData.PolySpouses)
                    {
                        if (ps.Value.Contains(n.Name))
                        {
                            taken = true;
                            //Monitor.Log("Removing polyspouse: " + n.Name);
                        } //belongs to someone in poly
                    }
                }
                if(!taken) NPCs.Add(n);
            }
            Monitor.Log("Found " + NPCs.Count + " polygamy marriage candidates.");

            //pick a random spouse for me today
            if (PolyData.PolySpouses.ContainsKey(Game1.player.UniqueMultiplayerID))
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
                }
                //distribute remaining spouses
                foreach (var otherSpouseName in PolyData.PolySpouses[Game1.player.UniqueMultiplayerID])
                {
                    NPC otherSpouse = Game1.getCharacterFromName(otherSpouseName);
                    //find a free tile to position them on
                    GameLocation l = Game1.player.currentLocation;
                    var p = FindSpotForNPC(l, l is StardewValley.Locations.FarmHouse);
                    if(p != Point.Zero)
                    {
                        ModUtil.WarpNPC(otherSpouse, l, p);
                    }
                }
                //add yesterday's spouse back to poly spouses
                if (actualSpouseYesterday != null) PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Add(actualSpouseYesterday);
            }
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

        public Point FindSpotForNPC(GameLocation l, bool checkVanillaHouseWalls)
        {
            Point randomPoint = Point.Zero;
            for (int i = 0; i < 100; i++)
            {
                int sizeX = l.map.GetLayer("Back").TileWidth;
                int sizeY = l.map.GetLayer("Back").TileHeight;
                randomPoint = new Point(ModUtil.RNG.Next(sizeX), ModUtil.RNG.Next(sizeY));
                bool unacceptable = false;
                unacceptable = (l.getTileIndexAt(randomPoint.X, randomPoint.Y, "Back") == -1 || !l.isTileLocationTotallyClearAndPlaceable(randomPoint.X, randomPoint.Y) || (checkVanillaHouseWalls && Utility.pointInRectangles(GetVanillaHouseWallRects(), randomPoint.X, randomPoint.Y)));
                //Monitor.Log("Testing position: " + randomPoint.X + " , " + randomPoint.Y + " : " + unacceptable);
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
                    var target = ModUtil.GetLocalPlayerFacingTileCoordinate();
                    var key = Game1.currentLocation.Name + "." + target[0] + "." + target[1];
                    //check if npc is in front of player
                    NPC tnpc = null;
                    foreach(NPC n in NPCs)
                    {
                        if(n.getTileX() == target[0] && n.getTileY() == target[1] && n.currentLocation != null && n.currentLocation.Name == Game1.currentLocation.Name)
                        {
                            tnpc = n;
                            break;
                        }
                    }
                    if (tnpc == null) return;
                    //if so we can override the dialogue here if conditions are met
                    var responses = new[]
                    {
                        new Response(tnpc.Name, "Yes pls"),
                        new Response("keyNo", "NO WAY!!1!")
                    };
                    Game1.currentLocation.createQuestionDialogue("awahoo with " + tnpc.displayName + "?", responses, Callback, tnpc);
                }
            }
        }

        public void Callback(Farmer f, string k)
        {
            if(k != "keyNo")
            {
                //demote current spouse to side piece\
                if(Game1.player.HouseUpgradeLevel < 3) Game1.player.HouseUpgradeLevel = 3; //prevent crash
                if (Game1.player.spouse != null)
                {
                    //push the spouse into a side slot
                    if (!PolyData.PolySpouses.ContainsKey(Game1.player.UniqueMultiplayerID))
                    {
                        PolyData.PolySpouses[Game1.player.UniqueMultiplayerID] = new List<string>();
                    }
                    PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Add(Game1.player.spouse);
                }
                //marry the new one while they're still interesting
                //WITH wedding ceremony (as opposed to hotswapping without)
                ModUtil.SetFriendshipPoints(k, 2500);
                Game1.player.spouse = k;
                PolyData.PrimarySpouse = k;
                Game1.player.friendshipData[Game1.player.spouse].WeddingDate = null;
                Game1.weddingToday = true;
                Game1.player.friendshipData[Game1.player.spouse].Status = FriendshipStatus.Engaged;
                Game1.checkForWedding();
                Game1.player.friendshipData[Game1.player.spouse].Status = FriendshipStatus.Married;
            }
            else Game1.showRedMessage("No want awahoo.");
        }
    }
}