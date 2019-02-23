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
            Monitor.Log("Found " + NPCs.Count + " poly marriage candidates.");

            //pick a random spouse for me today
            if (PolyData.PolySpouses.ContainsKey(Game1.player.UniqueMultiplayerID))
            {
                Random rng = new Random(DateTime.Now.Millisecond);
                //pick a random
                var nextSpouse = PolyData.PolySpouses[Game1.player.UniqueMultiplayerID][rng.Next(PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Count)];
                var lastSpouse = Game1.player.spouse;
                PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Remove(nextSpouse);
                if(lastSpouse != null) PolyData.PolySpouses[Game1.player.UniqueMultiplayerID].Add(lastSpouse);
                Monitor.Log("Poly spouse roll of the day: " + nextSpouse);
                Game1.player.spouse = nextSpouse; //HOTSWAP
                (Game1.getLocationFromName(Game1.player.homeLocation.Value) as StardewValley.Locations.FarmHouse).owner.spouse = nextSpouse;

                PolyData.PrimarySpouse = nextSpouse;
                Utility.getHomeOfFarmer(Game1.player).showSpouseRoom();
                Game1.getFarm().addSpouseOutdoorArea(Game1.player.spouse);
                //npc.isBirthday - maybe we sohuld guarantee birthday spouses
                //bring the others into the house
                NPC mainSpouse = Game1.getCharacterFromName(Game1.player.spouse);
                //place main spouse
                var mainSpouseBedSpot = (Game1.getLocationFromName(Game1.player.homeLocation.Value) as StardewValley.Locations.FarmHouse).getSpouseBedSpot();
                mainSpouse.setTileLocation(new Vector2(mainSpouseBedSpot.X, mainSpouseBedSpot.Y));

                foreach (var otherSpouseName in PolyData.PolySpouses[Game1.player.UniqueMultiplayerID])
                {
                    NPC otherSpouse = Game1.getCharacterFromName(otherSpouseName);
                    otherSpouse.currentLocation = mainSpouse.currentLocation;
                    //find a free tile to position them on
                    GameLocation l = mainSpouse.currentLocation;
                    if (l is StardewValley.Locations.FarmHouse) //has wonky collision
                    {
                        var lfh = l as StardewValley.Locations.FarmHouse;
                        var p = lfh.getRandomOpenPointInHouse(rng, 1, 50);
                        otherSpouse.setTilePosition((int)p.X, (int)p.Y);
                        Monitor.Log("Placed " + otherSpouse.Name + " at " + p.X + ", " + p.Y);
                    } 
                    else //no clue where we're sleeping. but let's roll with it.
                    {
                        for (int num = 50; num > 0; num--) //50 tries to place this spouse
                        {
                            var spouseSpawnPos = new Vector2(rng.Next(5, l.map.GetLayer("Back").TileWidth - 4), rng.Next(5, l.map.GetLayer("Back").TileHeight - 4));
                            if (l.isTileLocationTotallyClearAndPlaceable(spouseSpawnPos) && l.isCharacterAtTile(spouseSpawnPos) == null)
                            {
                                otherSpouse.setTilePosition((int)spouseSpawnPos.X, (int)spouseSpawnPos.Y);
                                Monitor.Log("Placed " + otherSpouse.Name + " at " + (int)spouseSpawnPos.X + ", " + (int)spouseSpawnPos.Y);
                            }
                        }
                    }
                }
            }
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