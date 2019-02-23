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
            FindAllWarpRoutes();
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
                    var p = FindSpotForNPC(l, l is StardewValley.Locations.FarmHouse);
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

        Dictionary<int, SchedulePathDescription> MakePolySchedule(NPC npc)
        {
            Dictionary<string, string> dictionary = null;
            try
            {
                dictionary = Game1.content.Load<Dictionary<string, string>>("Characters\\schedules\\" + npc.Name);
                npc.followSchedule = true;
            }
            catch (Exception)
            {
                return null;
            }
            if (ModUtil.RNG.Next(10) > 1) //is married? 1/10 chance of following pre-marriage schedule
            {
                string text = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                if ((npc.Name.Equals("Penny") && (text.Equals("Tue") || text.Equals("Wed") || text.Equals("Fri"))) || (npc.Name.Equals("Maru") && (text.Equals("Tue") || text.Equals("Thu"))) || (npc.Name.Equals("Harvey") && (text.Equals("Tue") || text.Equals("Thu"))))
                {
                    return parseMasterSchedule(npc, true, dictionary["marriageJob"]);
                } else if (!Game1.isRaining && dictionary.ContainsKey("marriage_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
                {
                    return parseMasterSchedule(npc, true, dictionary["marriage_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)]);
                } else
                {
                    //broken, do nothing
                    npc.followSchedule = false;
                    return null;
                }
            }
            if (dictionary.ContainsKey(Game1.currentSeason + "_" + Game1.dayOfMonth))
            {
                return parseMasterSchedule(npc, false, dictionary[Game1.currentSeason + "_" + Game1.dayOfMonth]);
            }
            int num;
            for (num = (Game1.player.friendshipData.ContainsKey(npc.Name) ? (Game1.player.friendshipData[npc.Name].Points / 250) : (-1)); num > 0; num--)
            {
                if (dictionary.ContainsKey(Game1.dayOfMonth + "_" + num))
                {
                    return parseMasterSchedule(npc, false, dictionary[Game1.dayOfMonth + "_" + num]);
                }
            }
            if (dictionary.ContainsKey(string.Empty + Game1.dayOfMonth))
            {
                return parseMasterSchedule(npc, false, dictionary[string.Empty + Game1.dayOfMonth]);
            }
            if (npc.Name.Equals("Pam") && Game1.player.mailReceived.Contains("ccVault"))
            {
                return parseMasterSchedule(npc, false, dictionary["bus"]);
            }
            if (Game1.isRaining)
            {
                if (Game1.random.NextDouble() < 0.5 && dictionary.ContainsKey("rain2"))
                {
                    return parseMasterSchedule(npc, false, dictionary["rain2"]);
                }
                if (dictionary.ContainsKey("rain"))
                {
                    return parseMasterSchedule(npc, false, dictionary["rain"]);
                }
            }
            List<string> list = new List<string>
            {
                Game1.currentSeason,
                Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)
            };
            num = (Game1.player.friendshipData.ContainsKey(npc.Name) ? (Game1.player.friendshipData[npc.Name].Points / 250) : (-1));
            while (num > 0)
            {
                list.Add(string.Empty + num);
                if (dictionary.ContainsKey(string.Join("_", list)))
                {
                    return parseMasterSchedule(npc, false, dictionary[string.Join("_", list)]);
                }
                num--;
                list.RemoveAt(list.Count - 1);
            }
            if (dictionary.ContainsKey(string.Join("_", list)))
            {
                return parseMasterSchedule(npc, false, dictionary[string.Join("_", list)]);
            }
            if (dictionary.ContainsKey(Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
            {
                return parseMasterSchedule(npc, false, dictionary[Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)]);
            }
            if (dictionary.ContainsKey(Game1.currentSeason))
            {
                return parseMasterSchedule(npc, false, dictionary[Game1.currentSeason]);
            }
            if (dictionary.ContainsKey("spring_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
            {
                return parseMasterSchedule(npc, false, dictionary["spring_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)]);
            }
            list.RemoveAt(list.Count - 1);
            list.Add("spring");
            num = (Game1.player.friendshipData.ContainsKey(npc.Name) ? (Game1.player.friendshipData[npc.Name].Points / 250) : (-1));
            while (num > 0)
            {
                list.Add(string.Empty + num);
                if (dictionary.ContainsKey(string.Join("_", list)))
                {
                    return parseMasterSchedule(npc, false, dictionary[string.Join("_", list)]);
                }
                num--;
                list.RemoveAt(list.Count - 1);
            }
            if (dictionary.ContainsKey("spring"))
            {
                return parseMasterSchedule(npc, false, dictionary["spring"]);
            }
            return null;
        }

        private Dictionary<int, SchedulePathDescription> parseMasterSchedule(NPC npc, bool married, string rawData)
        {
            string[] array = rawData.Split('/');
            Dictionary<int, SchedulePathDescription> dictionary = new Dictionary<int, SchedulePathDescription>();
            int num = 0;
            if (array[0].Contains("GOTO"))
            {
                string text = array[0].Split(' ')[1];
                if (text.ToLower().Equals("season"))
                {
                    text = Game1.currentSeason;
                }
                try
                {
                    array = Game1.content.Load<Dictionary<string, string>>("Characters\\schedules\\" + npc.Name)[text].Split('/');
                }
                catch (Exception)
                {
                    return parseMasterSchedule(npc, married, Game1.content.Load<Dictionary<string, string>>("Characters\\schedules\\" + npc.Name)["spring"]);
                }
            }
            if (array[0].Contains("NOT"))
            {
                string[] array2 = array[0].Split(' ');
                string a = array2[1].ToLower();
                if (a == "friendship")
                {
                    string name = array2[2];
                    int num2 = Convert.ToInt32(array2[3]);
                    bool flag = false;
                    foreach (Farmer allFarmer in Game1.getAllFarmers())
                    {
                        if (allFarmer.getFriendshipLevelForNPC(name) >= num2)
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag)
                    {
                        return parseMasterSchedule(npc, married, Game1.content.Load<Dictionary<string, string>>("Characters\\schedules\\" + npc.Name)["spring"]);
                    }
                    num++;
                }
            }
            if (array[num].Contains("GOTO"))
            {
                string text2 = array[num].Split(' ')[1];
                if (text2.ToLower().Equals("season"))
                {
                    text2 = Game1.currentSeason;
                }
                array = Game1.content.Load<Dictionary<string, string>>("Characters\\schedules\\" + npc.Name)[text2].Split('/');
                num = 1;
            }
            Point point = married ? new Point(0, 23) : new Point((int)npc.DefaultPosition.X / 64, (int)npc.DefaultPosition.Y / 64);
            string text3 = married ? "BusStop" : ((string)npc.DefaultMap);
            for (int i = num; i < array.Length; i++)
            {
                if (array.Length <= 1)
                {
                    break;
                }
                int num3 = 0;
                string[] array3 = array[i].Split(' ');
                int key = Convert.ToInt32(array3[num3]);
                num3++;
                string locationName = array3[num3];
                string endBehavior = null;
                string endMessage = null;
                if (int.TryParse(locationName, out int _))
                {
                    locationName = text3;
                    num3--;
                }
                num3++;
                int tileX = Convert.ToInt32(array3[num3]);
                num3++;
                int tileY = Convert.ToInt32(array3[num3]);
                num3++;
                int num4 = 2;
                try
                {
                    num4 = Convert.ToInt32(array3[num3]);
                    num3++;
                }
                catch (Exception)
                {
                    num4 = 2;
                }
                if (num3 < array3.Length)
                {
                    if (array3[num3].Length > 0 && array3[num3][0] == '"')
                    {
                        endMessage = array[i].Substring(array[i].IndexOf('"'));
                    }
                    else
                    {
                        endBehavior = array3[num3];
                        num3++;
                        if (num3 < array3.Length && array3[num3].Length > 0 && array3[num3][0] == '"')
                        {
                            endMessage = array[i].Substring(array[i].IndexOf('"')).Replace("\"", "");
                        }
                    }
                }
                dictionary.Add(key, pathfindToNextScheduleLocation(npc, text3, point.X, point.Y, locationName, tileX, tileY, num4, endBehavior, endMessage));
                point.X = tileX;
                point.Y = tileY;
                text3 = locationName;
            }
            return dictionary;
        }

        private static bool doesRoutesListContain(List<string> route)
        {
            foreach (List<string> item in warpRoutes)
            {
                if (item.Count == route.Count)
                {
                    bool flag = true;
                    for (int i = 0; i < route.Count; i++)
                    {
                        if (!item[i].Equals(route[i], StringComparison.Ordinal))
                        {
                            flag = false;
                            break;
                        }
                    }
                    if (flag)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        private static List<List<string>> warpRoutes;
        public static void FindAllWarpRoutes()
        {
            warpRoutes = new List<List<string>>();
            foreach (GameLocation location in Game1.locations)
            {
                if (!(location is Farm) && !location.Name.Equals("Backwoods"))
                {
                    List<string> route = new List<string>();
                    GetWarpRoutes(location, route);
                }
            }
        }
        private static bool GetWarpRoutes(GameLocation l, List<string> route)
        {
            bool result = false;
            if (l != null && !route.Contains(l.Name, StringComparer.Ordinal))
            {
                route.Add(l.Name);
                if (route.Count == 1 || !doesRoutesListContain(route))
                {
                    if (route.Count > 1)
                    {
                        warpRoutes.Add(route.ToList());
                        result = true;
                    }
                    foreach (Warp warp in l.warps)
                    {
                        string targetName = warp.TargetName;
                        if (!targetName.Equals("Farm", StringComparison.Ordinal) && !targetName.Equals("Woods", StringComparison.Ordinal) && !targetName.Equals("Backwoods", StringComparison.Ordinal) && !targetName.Equals("Tunnel", StringComparison.Ordinal))
                        {
                            GetWarpRoutes(Game1.getLocationFromName(targetName), route);
                        }
                    }
                    foreach (Point key in l.doors.Keys)
                    {
                        GetWarpRoutes(Game1.getLocationFromName(l.doors[key]), route);
                    }
                }
                if (route.Count > 0)
                {
                    route.RemoveAt(route.Count - 1);
                }
            }
            return result;
        }

        private List<string> getLocationRoute(string startingLocation, string endingLocation)
        {
            foreach (List<string> item in warpRoutes)
            {
                if (item.First().Equals(startingLocation, StringComparison.Ordinal) && item.Last().Equals(endingLocation, StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return null;
        }

        private SchedulePathDescription pathfindToNextScheduleLocation(NPC npc, string startingLocation, int startingX, int startingY, string endingLocation, int endingX, int endingY, int finalFacingDirection, string endBehavior, string endMessage)
        {
            Stack<Point> stack = new Stack<Point>();
            Point startPoint = new Point(startingX, startingY);
            List<string> list = (!startingLocation.Equals(endingLocation, StringComparison.Ordinal)) ? getLocationRoute(startingLocation, endingLocation) : null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    GameLocation locationFromName = Game1.getLocationFromName(list[i]);
                    if (locationFromName.Name.Equals("Trailer") && Game1.MasterPlayer.mailReceived.Contains("pamHouseUpgrade"))
                    {
                        locationFromName = Game1.getLocationFromName("Trailer_Big");
                    }
                    if (i < list.Count - 1)
                    {
                        Point warpPointTo = locationFromName.getWarpPointTo(list[i + 1]);
                        if (warpPointTo.Equals(Point.Zero) || startPoint.Equals(Point.Zero))
                        {
                            throw new Exception("schedule pathing tried to find a warp point that doesn't exist.");
                        }
                        stack = addToStackForSchedule(stack, PathFindController.findPathForNPCSchedules(startPoint, warpPointTo, locationFromName, 30000));
                        startPoint = locationFromName.getWarpPointTarget(warpPointTo);
                    }
                    else
                    {
                        stack = addToStackForSchedule(stack, PathFindController.findPathForNPCSchedules(startPoint, new Point(endingX, endingY), locationFromName, 30000));
                    }
                }
            }
            else if (startingLocation.Equals(endingLocation, StringComparison.Ordinal))
            {
                stack = PathFindController.findPathForNPCSchedules(startPoint, new Point(endingX, endingY), Game1.getLocationFromName(startingLocation), 30000);
            }
            return new SchedulePathDescription(stack, finalFacingDirection, endBehavior, endMessage);
        }

        private Stack<Point> addToStackForSchedule(Stack<Point> original, Stack<Point> toAdd)
        {
            if (toAdd == null)
            {
                return original;
            }
            original = new Stack<Point>(original);
            while (original.Count > 0)
            {
                toAdd.Push(original.Pop());
            }
            return toAdd;
        }

        public void FixSpouseSchedule(GameLocation l, NPC npc, bool poly = false)
        {
            if (poly)
            {
                npc.DefaultPosition = new Vector2(npc.getTileX() * 64, npc.getTileY() * 64);
                npc.DefaultMap = npc.currentLocation.Name;
                //npc.clearSchedule();
                //npc.performBehavior(1);
                npc.Schedule = MakePolySchedule(npc);
                //npc.ignoreScheduleToday = true;
                npc.followSchedule = true;
                //npc.endOfRouteMessage.Value = null;
                //npc.marriageDuties();
                //npc.checkSchedule(Game1.timeOfDay);
                //var p = FindSpotForNPC(l, l is StardewValley.Locations.FarmHouse);
                //npc.controller = new PathFindController(npc, l, new Point((int)p.X, (int)p.Y), ModUtil.RNG.Next(4));
                //npc.reloadSprite();
            } else
            {
                /*
                string text = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
                if ((npc.Name.Equals("Penny") && (text.Equals("Tue") || text.Equals("Wed") || text.Equals("Fri"))) || (npc.Name.Equals("Maru") && (text.Equals("Tue") || text.Equals("Thu"))) || (npc.Name.Equals("Harvey") && (text.Equals("Tue") || text.Equals("Thu"))))
                {
                    npc.setNewDialogue("MarriageDialogue", "jobLeave_", -1, add: false, clearOnMovement: true);
                }
                if (!Game1.isRaining && npc.dictionary.ContainsKey("marriage_" + Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)))
                {
                    npc.setNewDialogue("MarriageDialogue", "funLeave_", -1, add: false, clearOnMovement: true);
                }
                npc.followSchedule = false;
                npc.endOfRouteMessage.Value = null;
                if (!Game1.player.divorceTonight.Value) npc.marriageDuties();
                */
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