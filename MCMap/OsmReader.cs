using Kaos.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace MinecraftMapper {
    class OsmReader {
        public readonly Dictionary<long, Node> Nodes = new Dictionary<long, Node>();
        public readonly Dictionary<long, Way> Ways = new Dictionary<long, Way>();
        public readonly Dictionary<string, List<Way>> RoadsByName = new Dictionary<string, List<Way>>();
        public readonly Dictionary<long, Building> Buildings = new Dictionary<long, Building>();
        public readonly HashSet<Barrier> Barriers = new HashSet<Barrier>();
        public readonly Dictionary<long, Building> SeattleBuildings = new Dictionary<long, Building>();
        public readonly Dictionary<string, Building> BuildingsByAddress = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Node>> BusStops = new Dictionary<string, List<Node>>();
        //public readonly Dictionary<long, SeattleNode> AddressNodes = new Dictionary<long, SeattleNode>();
        public readonly Dictionary<Node, SignType> Signs = new Dictionary<Node, SignType>();
        public readonly RankedDictionary<double, RankedDictionary<double, SeattleNode>> OrderedNodes = new RankedDictionary<double, RankedDictionary<double, SeattleNode>>();
        public readonly RankedDictionary<double, RankedDictionary<double, List<Way>>> OrderedWays = new RankedDictionary<double, RankedDictionary<double, List<Way>>>();
        private readonly string _sourceXml;

        public OsmReader(string sourceXml) {
            _sourceXml = sourceXml;
        }

        public class Node {
            public readonly double Lat, Long;

            public Node(double lat, double lng) {
                Lat = lat;
                Long = lng;
            }

            public override string ToString() {
                return string.Format("{0},{1}", Lat, Long);
            }
        }

        public class SeattleNode : Node {
            public readonly string HouseNumber, Street, Name;
            public long NodeNumber;
            public SeattleNode(double lat, double lng, long nodeNumber, string name, string houseNumber, string street, int? stories) : base(lat, lng) {
                HouseNumber = houseNumber;
                Name = name;
                Street = street;
                NodeNumber = nodeNumber;
            }

            public override string ToString() {
                return string.Format("{0},{1} {2} {3}", Lat, Long, HouseNumber, Street);
            }
        }

        public enum CrossingType {
            None,
            Zebra,
            TrafficSignals,
            Uncontrolled
        }

        public enum SignType {
            None,
            StreetLamp,
            TrafficSignal
        }

        public enum RoadType {
            None,
            Residential,
            FootWay,
            CycleWay,
            MotorwayLink,
            Motorway,
            Service,
            Primary,
            Secondary,
            Path,
            Trunk,
            BusStop,
            Crossing
        }

        public enum BuildingType {
            None,
            Yes,
            University,
            Commercial,
            Apartments,
            Roof,
            Unknown,
            Hotel,
            Retail,
            Industrial,
            Office
        }

        public enum Amenity {
            None,
            School,
            Parking,
            Bank,
            CommunityCenter,
            Toilets,
            Pub,
            PlaceOfWorship,
            Restaurant,
            Shelter,
            Hospital,
            FastFood,
            Library,
            Theatre,
            Clinic,
            Cafe,
            FireStation,
            TrainStation,
            University,
            Police,
            PostOffice,
            Prison
        }

        public enum Sidewalk {
            None, Both,
            Left,
            Right
        }

        public enum Surface {
            None,
            Asphalt,
            Concrete,
            Cement,
            Paved,
            Cobblestone,
            Wood,
            PavingStones,
            Brick,
            Unpaved,
            Grass,
            Ground,
            Dirt,
            Gravel,
            Metal,
            Clay,
            Compacted,
            FineGravel,
            RailBed,
            RipRap,
            ArtificalTurf,
            Sett,
            Pebbles,
            GravelGrass,
            Sand,
            GrassPaver,
            MetalGrid,
            PebbleStone,
            Rock,
            Woodchips,
            Stone,
            RailroadTies,
        }

        public class Way {
            public readonly Node[] Nodes;
            public readonly int? Lanes;
            public readonly RoadType RoadType;
            public readonly string Name;
            public readonly int? Layer;
            public readonly Sidewalk Sidewalk;
            public readonly CrossingType Crossing;
            public readonly Surface Surface;

            public Way(string name, Node[] nodes, int? lanes, RoadType roadType, Sidewalk sidewalk, int? layer, CrossingType crossing, Surface surface) {
                Name = name;
                Nodes = nodes;
                Lanes = lanes;
                RoadType = roadType;
                Sidewalk = sidewalk;
                Layer = layer;
                Crossing = crossing;
                Surface = surface;
            }
        }

        public class Building {
            public readonly string HouseNumber, Street, Name;
            public readonly Node[] Nodes;
            public readonly Amenity Amenity;
            public float? Stories;
            public double? PrimaryLat;
            public double? PrimaryLong;
            public readonly Material Material;

            public Building(string name, string houseNumber, string street, Amenity amenity, Material material, Node[] nodes) {
                Name = name;
                HouseNumber = houseNumber;
                Street = street;
                Amenity = amenity;
                Nodes = nodes;
                Material = material;
            }

            public Building(string name, string houseNumber, string street, Amenity amenity, Material material, double lat, double longitude, Node[] nodes) {
                Name = name;
                HouseNumber = houseNumber;
                Street = street;
                Amenity = amenity;
                Material = material;
                PrimaryLat = lat;
                PrimaryLong = longitude;
                Nodes = nodes;
            }
        }

        // https://stackoverflow.com/questions/17789271/get-xelement-attribute-value
        public static IEnumerable<XElement> ElementsNamed(XmlReader reader, HashSet<string> elements) {
            reader.MoveToContent(); // will not advance reader if already on a content node; if successful, ReadState is Interactive
            reader.Read();          // this is needed, even with MoveToContent and ReadState.Interactive
            while (!reader.EOF && reader.ReadState == ReadState.Interactive) {
                // corrected for bug noted by Wes below...
                if (reader.NodeType == XmlNodeType.Element && elements.Contains(reader.Name)) {
                    // this advances the reader...so it's either XNode.ReadFrom() or reader.Read(), but not both
                    var matchedElement = XNode.ReadFrom(reader) as XElement;
                    if (matchedElement != null)
                        yield return matchedElement;
                } else
                    reader.Read();
            }
        }

        public void ReadData() {            
            var reader = XmlReader.Create(new FileStream(_sourceXml, FileMode.Open));
            HashSet<string> nodeNames = new HashSet<string>() {
                { "node" },
                { "way" },
            };
            foreach (var data in ElementsNamed(reader, nodeNames)) {
                TagInfo tags = new TagInfo();
                switch (data.Name.LocalName) {
                    case "node":                        
                        var id = Convert.ToInt64(data.Attribute("id").Value);
                        var lat = Convert.ToDouble(data.Attribute("lat").Value);
                        var longitude = Convert.ToDouble(data.Attribute("lon").Value);
                        foreach (var node in data.Descendants()) {
                            switch (node.Name.LocalName) {
                                case "tag":
                                    ReadTag(ref tags, node);
                                    break;
                            }
                        }
                        if (tags.roadType == RoadType.BusStop) {
                            if (tags.name != null && (tags.shelter ?? false)) {
                                var streetNames = tags.name.Split('&');
                                if (streetNames.Length != 2) {
                                    Console.WriteLine("BS: {0}", tags.name);
                                }
                                var normalized = NormalizeName(streetNames[0]);
                                List<Node> busNodes;
                                if (!BusStops.TryGetValue(normalized, out busNodes)) {
                                    BusStops[normalized] = busNodes = new List<Node>();
                                }
                                busNodes.Add(new Node(lat, longitude));
                            }
                        } else if (tags.signType != SignType.None) {
                            Signs[new Node(lat, longitude)] = tags.signType;
                        } else if (tags.houseNumber != null && tags.street != null) {
                            RankedDictionary<double, SeattleNode> longNodes;
                            if (!OrderedNodes.TryGetValue(lat, out longNodes)) {
                                longNodes = OrderedNodes[lat] = new RankedDictionary<double, SeattleNode>();
                            }
                            longNodes[longitude] = new SeattleNode(lat, longitude, id, tags.name, tags.houseNumber, tags.street, tags.stories);
                        }
                        Nodes[id] = new Node(lat, longitude);
                        break;
                    case "way":
                        List<Node> nodes = new List<Node>();
                        foreach (var node in data.Descendants()) {
                            switch (node.Name.LocalName) {
                                case "nd":
                                    nodes.Add(Nodes[Convert.ToInt64(node.Attribute("ref").Value)]);
                                    break;
                                case "tag":
                                    ReadTag(ref tags, node);
                                    break;
                            }
                        }

                        if (tags.building != BuildingType.None) {
                            var buildingObj = Buildings[Convert.ToInt64(data.Attribute("id").Value)] = new Building(tags.name, tags.houseNumber, tags.street, tags.amenity, tags.material, nodes.ToArray());
                            buildingObj.Stories = tags.stories;
                            if (tags.houseNumber != null && tags.street != null) {
                                BuildingsByAddress[tags.houseNumber + " " + tags.street] = buildingObj;
                            }

                            double maxLat = double.MinValue, minLat = double.MaxValue, maxLong = double.MinValue, minLong = double.MaxValue;
                            foreach (var point in nodes) {
                                maxLat = Math.Max(point.Lat, maxLat);
                                minLat = Math.Min(point.Lat, minLat);
                                maxLong = Math.Max(point.Long, maxLong);
                                minLong = Math.Min(point.Long, minLong);
                            }

                            int itemsCount = 0;
                            foreach (var group in OrderedNodes.ElementsBetween(minLat, maxLat)) {
                                foreach (var longAndNode in group.Value.ElementsBetween(minLong, maxLong)) {
                                    var node = longAndNode.Value;
                                    if (node.Lat >= minLat && node.Lat <= maxLat &&
                                        node.Long >= minLong && node.Long <= maxLong) {
                                        var buildingFromPoint = Buildings[node.NodeNumber] = new Building(
                                            node.Name ?? tags.name,
                                            node.HouseNumber,
                                            node.Street,
                                            tags.amenity,
                                            tags.material,
                                            node.Lat,
                                            node.Long,
                                            nodes.ToArray());
                                        buildingFromPoint.Stories = tags.stories;
                                        itemsCount++;
                                        Console.WriteLine("ByAddressNode: " + node.HouseNumber + " " + node.Street + " (" + itemsCount + ")");
                                        BuildingsByAddress[node.HouseNumber + " " + node.Street] = buildingFromPoint;
                                    }
                                }
                            }
                        } else if (tags.roadType != RoadType.None) {
                            var road = Ways[Convert.ToInt64(data.Attribute("id").Value)] = new Way(
                                tags.name,
                                nodes.ToArray(),
                                tags.lanes,
                                tags.roadType,
                                tags.sidewalk,
                                tags.layer,
                                tags.crossing,
                                tags.surface
                            );
                            foreach (var point in nodes) {
                                RankedDictionary<double, List<Way>> orderedWays = new RankedDictionary<double, List<Way>>();
                                if (!OrderedWays.TryGetValue(point.Lat, out orderedWays)) {
                                    OrderedWays[point.Lat] = orderedWays = new RankedDictionary<double, List<Way>>();
                                }
                                List<Way> ways;
                                if (!orderedWays.TryGetValue(point.Long, out ways)) {
                                    orderedWays[point.Long] = ways = new List<Way>();
                                }
                                ways.Add(road);
                            }
                            if (tags.name != null) {
                                List<Way> roads;
                                if (!RoadsByName.TryGetValue(tags.name, out roads)) {
                                    roads = RoadsByName[tags.name] = new List<Way>();
                                }
                                roads.Add(road);
                            }
                        } else if (tags.barrier != BarrierKind.None) {
                            Barriers.Add(new Barrier(nodes.ToArray(), tags.barrier));
                        }
                        break;
                }
            }
        }

        public class Barrier {
            public readonly Node[] Nodes;
            public readonly BarrierKind Kind;

            public Barrier(Node[] nodes, BarrierKind kind) {
                Nodes = nodes;
                Kind = kind;
            }
        }

        private string NormalizeName(string name) {
            name = name.Trim();
            if (name.StartsWith("NE ")) {
                name = "Northeast " + name.Substring(3);
            }
            if (name.StartsWith("NW ")) {
                name = "Northwest " + name.Substring(3);
            }
            if (name.StartsWith("SE ")) {
                name = "Southeast " + name.Substring(3);
            }
            if (name.StartsWith("SW ")) {
                name = "Southwest " + name.Substring(3);
            }
            if (name.StartsWith("E ")) {
                name = "East " + name.Substring(2);
            }
            if (name.StartsWith("W ")) {
                name = "West " + name.Substring(2);
            }
            if (name.StartsWith("S ")) {
                name = "South " + name.Substring(2);
            }
            if (name.StartsWith("N ")) {
                name = "North " + name.Substring(2);
            }
            if (name.EndsWith(" NE")) {
                name = name.Substring(0, name.Length - 3) + " Northeast";
            }
            if (name.EndsWith(" NW")) {
                name = name.Substring(0, name.Length - 3) + " Northwest";
            }
            if (name.EndsWith(" SE")) {
                name = name.Substring(0, name.Length - 3) + " Southeast";
            }
            if (name.EndsWith(" SW")) {
                name = name.Substring(0, name.Length - 3) + " Southwest";
            }
            if (name.EndsWith(" E")) {
                name = name.Substring(0, name.Length - 2) + " East";
            }
            if (name.EndsWith(" S")) {
                name = name.Substring(0, name.Length - 2) + " South";
            }
            if (name.EndsWith(" N")) {
                name = name.Substring(0, name.Length - 2) + " North";
            }
            if (name.EndsWith(" W")) {
                name = name.Substring(0, name.Length - 2) + " West";
            }
            if (name.EndsWith(" St")) {
                name = name.Substring(0, name.Length - 2) + "Street";
            }
            if (name.EndsWith(" Dr")) {
                name = name.Substring(0, name.Length - 2) + "Drive";
            }
            if (name.EndsWith(" Ave")) {
                name = name.Substring(0, name.Length - 3) + "Avenue";
            }
            if (name.EndsWith(" Blvd")) {
                name = name.Substring(0, name.Length - 4) + "Boulevard";
            }

            name = name.Replace(" St ", " Street ");
            name = name.Replace(" Ave ", " Avenue ");
            name = name.Replace(" Dr ", " Drive ");
            Console.WriteLine(name);
            return name;
        }

        struct TagInfo {
            public int? lanes;
            public Material material;
            public RoadType roadType;
            public string name;
            public BuildingType building;
            public int? layer;
            public string street;
            public string houseNumber;
            public Sidewalk sidewalk;
            public Amenity amenity;
            public string source;
            public int? stories;
            public bool? shelter;
            public CrossingType crossing;
            public BarrierKind barrier;
            public Surface surface;
            public SignType signType;
        }

        public enum BarrierKind {
            None,
            Fence,
            Wall,
            GuardRail,
            Gate,
            Hedge,
            RetainingWall
        }

        private static void ReadTag(ref TagInfo tagInfo, XElement node) {
            var key = node.Attribute("k").Value;
            var value = node.Attribute("v").Value;
            switch (key) {
                case "surface":
                    switch (value) {
                        case "asphalt;concrete": break;
                        case "asphalt": tagInfo.surface = Surface.Asphalt; break;
                        case "cement": tagInfo.surface = Surface.Cement; break;
                        case "concrete:plates":
                        case "concrete": tagInfo.surface = Surface.Concrete; break;
                        case "paved": tagInfo.surface = Surface.Paved; break;
                        case "cobblestone": tagInfo.surface = Surface.Cobblestone; break;
                        case "wood": tagInfo.surface = Surface.Wood; break;
                        case "paving_stones": tagInfo.surface = Surface.PavingStones; break;
                        case "bricks":
                        case "brick;asphalt;concrete": break;
                        case "brick": tagInfo.surface = Surface.Brick; break;
                        case "unpaved": tagInfo.surface = Surface.Unpaved;  break;
                        case "grass": tagInfo.surface = Surface.Grass; break;
                        case "ground": tagInfo.surface = Surface.Ground; break;
                        case "dirt": tagInfo.surface = Surface.Dirt; break;
                        case "gravel": tagInfo.surface = Surface.Gravel; break;
                        case "metal": tagInfo.surface = Surface.Metal; break;
                        case "clay": tagInfo.surface = Surface.Clay; break;
                        case "compacted": tagInfo.surface = Surface.Compacted; break;
                        case "fine_gravel": tagInfo.surface = Surface.FineGravel; break;
                        case "railbed": tagInfo.surface = Surface.RailBed; break;
                        case "riprap": tagInfo.surface = Surface.RipRap; break;
                        case "astr": 
                        case "artificial_turf": tagInfo.surface = Surface.ArtificalTurf; break;
                        case "sett": tagInfo.surface = Surface.Sett; break;
                        case "pebbles": tagInfo.surface = Surface.Pebbles; break;
                        case "sand": tagInfo.surface = Surface.Sand; break;
                        case "gravel;grass": tagInfo.surface = Surface.GravelGrass; break;
                        case "grass_paver": tagInfo.surface = Surface.GrassPaver; break;
                        case "metal_grid": tagInfo.surface = Surface.MetalGrid; break;
                        case "pebblestone": tagInfo.surface = Surface.PebbleStone; break;
                        case "rock": tagInfo.surface = Surface.Rock; break;
                        case "woodchips": tagInfo.surface = Surface.Woodchips; break;
                        case "stone": tagInfo.surface = Surface.Stone; break;
                        case "railroad_ties": tagInfo.surface = Surface.RailroadTies; break;
                        default:
                            Console.WriteLine("Unknown surface: {0}", value);
                            break;
                    }
                    break;
                case "barrier":
                    switch (value) {
                        case "fence": tagInfo.barrier = BarrierKind.Fence; break;
                        case "wall": tagInfo.barrier = BarrierKind.Wall; break;
                        case "guard_rail": tagInfo.barrier = BarrierKind.GuardRail; break;
                        case "gate": tagInfo.barrier = BarrierKind.Gate; break;
                        case "hedge": tagInfo.barrier = BarrierKind.Hedge; break;
                        case "retaining_wall": tagInfo.barrier = BarrierKind.RetainingWall; break;
                    }
                    break;
                case "source":
                    tagInfo.source = value;
                    break;
                case "height":
                    double height;
                    if (Double.TryParse(value, out height)) {
                        tagInfo.stories = (int)(height / 4);
                    }
                    break;
                case "building:material": tagInfo.material = ReadMaterial(value); break;
                case "building:levels":
                    int levels;
                    if (Int32.TryParse(value, out levels)) {
                        tagInfo.stories = levels;
                    }
                    break;
                case "sidewalk":
                    switch (value) {
                        case "both": tagInfo.sidewalk = Sidewalk.Both; break;
                        case "right": tagInfo.sidewalk = Sidewalk.Right; break;
                        case "left": tagInfo.sidewalk = Sidewalk.Left; break;
                    }
                    break;
                case "amenity":
                    tagInfo.amenity = ReadAmenity(value);
                    break;
                case "layer":
                    tagInfo.layer = Int32.Parse(value);
                    break;
                case "building":
                    tagInfo.building = ReadBuildingType(value);
                    break;
                case "addr:housenumber":
                    tagInfo.houseNumber = value;
                    break;
                case "addr:street":
                    tagInfo.street = value;
                    break;
                case "lanes":
                    tagInfo.lanes = Convert.ToInt32(value);
                    break;
                case "shelter":
                    switch (value) {
                        case "yes": tagInfo.shelter = true; break;
                        case "no": tagInfo.shelter = false; break;
                        default:
                            Console.WriteLine("Unknown shelter: " + value);
                            break;
                    }
                    break;
                case "crossing":
                    switch (value) {
                        case "uncontrolled;zebra":
                        case "zebra": tagInfo.crossing = CrossingType.Zebra; break;
                        case "traffic_signals": tagInfo.crossing = CrossingType.TrafficSignals; break;
                        case "uncontrolled": tagInfo.crossing = CrossingType.Uncontrolled; break;
                        case "unmarked": break;
                        case "no": break;
                        case "island": break;
                        case "yes": break;
                        case "traffic_signals;marked": break;
                        case "controlled": break;
                        case "pedestrian_signals": break;
                        case "marked": break;
                        default:
                            Console.WriteLine("Unknown crossing: " + value);
                            break;
                    }
                    break;
                case "highway":
                    switch (value) {
                        case "bus_stop": tagInfo.roadType = RoadType.BusStop; break;
                        case "service": tagInfo.roadType = RoadType.Service; break;
                        case "tertiary":
                        case "living_street":
                        case "residential": tagInfo.roadType = RoadType.Residential; break;
                        case "path": tagInfo.roadType = RoadType.Path; break;
                        case "footway": tagInfo.roadType = RoadType.FootWay; break;
                        case "cycleway": tagInfo.roadType = RoadType.CycleWay; break;
                        case "motorway_link": tagInfo.roadType = RoadType.MotorwayLink; break;
                        case "motorway": tagInfo.roadType = RoadType.Motorway; break;
                        case "primary": tagInfo.roadType = RoadType.Primary; break;
                        case "secondary": tagInfo.roadType = RoadType.Secondary; break;
                        case "trunk": tagInfo.roadType = RoadType.Trunk; break;
                        case "motorway_junction": break;
                        case "traffic_signals": break;
                        case "crossing": tagInfo.roadType = RoadType.Crossing; break;
                        case "stop": break;
                        case "turning_circle": break;
                        case "elevator": break;
                        case "give_way": break;
                        case "turning_loop": break;
                        case "mini_roundabout": break;
                        case "passing_place":
                            break;
                        case "street_lamp":  tagInfo.signType = SignType.StreetLamp;  break;
                        case "traffic_sign": tagInfo.signType = SignType.TrafficSignal; break;
                        case "noexit": break;
                        case "milestone": break;
                        case "steps": break;
                        case "track": break;
                        case "trunk_link": break;
                        case "primary_link": break;
                        case "secondary_link": break;
                        case "tertiary_link": break;
                        case "unclassified": break;
                        case "pedestrian": break;
                        case "construction": break;
                        case "abandoned": break;
                        case "road": break;
                        case "corridor": break;
                        case "proposed":
                        default:
                            Console.WriteLine("Unknown highway: " + value);
                            break;
                    }
                    break;
                case "name":
                    tagInfo.name = value;
                    break;
            }
        }

        private static BuildingType ReadBuildingType(string value) {
            switch (value) {
                case "yes": return BuildingType.Yes;
                case "university": return BuildingType.University;
                case "commercial": return BuildingType.Commercial;
                case "apartments": return BuildingType.Apartments;
                case "roof": return BuildingType.Roof;
                case "hotel": return BuildingType.Hotel;
                case "retail": return BuildingType.Retail;
                case "industrial": return BuildingType.Industrial;
                case "office": return BuildingType.Office;
                case "no": return BuildingType.None;
                case "residential":
                case "terrace": return BuildingType.Unknown;
            }
            return BuildingType.Unknown;
        }

        public enum Material {
            None,
            Sandstone,
            Brick
        }

        private static Material ReadMaterial(string value) {
            switch (value) {
                case "sandstone": return Material.Sandstone;
                case "brick": return Material.Brick;
            }
            return Material.None;
        }

        private static Amenity ReadAmenity(string value) {
            Amenity amenity = Amenity.None;
            switch (value) {
                case "school": amenity = Amenity.School; break;
                case "parking": amenity = Amenity.Parking; break;
                case "bank": amenity = Amenity.Bank; break;
                case "community_center": amenity = Amenity.CommunityCenter; break;
                case "toilets": amenity = Amenity.Toilets; break;
                case "pub": amenity = Amenity.Pub; break;
                case "place_of_worship": amenity = Amenity.PlaceOfWorship; break;
                case "restaurant": amenity = Amenity.Restaurant; break;
                case "shelter": amenity = Amenity.Shelter; break;
                case "hospital": amenity = Amenity.Hospital; break;
                case "fast_food": amenity = Amenity.FastFood; break;
                case "library": amenity = Amenity.Library; break;
                case "theatre": amenity = Amenity.Theatre; break;
                case "clinic": amenity = Amenity.Clinic; break;
                case "cafe": amenity = Amenity.Cafe; break;
                case "train_station": amenity = Amenity.TrainStation; break;
                case "fast_foot": break;
                case "fire_station": amenity = Amenity.FireStation; break;
                case "police": amenity = Amenity.Police; break;
                case "post_office": amenity = Amenity.PostOffice; break;
                case "prison": amenity = Amenity.Prison; break;
                case "fuel": break;
                case "cinema": break;
                case "parking_entrance": break;
                case "ferry_terminal": break;
                case "post_box": break;
                case "atm":
                case "grave_yard":
                case "kindergarten": break;
                case "arts_centre": break;
                case "public_building": break;
                case "fountain": break;
                case "drinking_water": break;
                case "bicycle_parking": break;
                case "bench": break;
                case "waste_basket": break;
                case "pharmacy": break;
                case "doctors": break;
                case "financial_advice": break;
                case "dentist": break;
                case "car_rental": break;
                case "telephone": break;
                case "ice_cream": break;
                case "bar": break;
                case "vending_machine": break;
                case "social_facility": break;
                case "veterinary": break;
                case "nightclub": break;
                case "community_centre": break;
                case "bus_station": break;
                case "car_wash": break;
                case "college": break;
                case "boat_rental": break;
                case "car_sharing": break;
                case "marketplace": break;
                case "bbq": break;
                case "boat_sales": break;
                case "townhall": break;
                case "recycling": break;
                case "food_court": break;
                case "biergarten": break;
                case "dojo": break;
                case "social_centre": break;
                case "dancing_school": break;
                case "orthodontics": break;
                case "hookah_lounge": break;
                case "chiropractic":
                case "chiropractor": break;
                case "art_gallery": break;
                case "lawyer": break;
                case "spa": break;
                case "parking_space": break;
                case "classes": break;
                case "self_storage": break;
                case "studio": break;
                case "embassy": break;
                case "driving_school": break;
                case "health_club": break;
                case "shower": break;
                case "nursing_home": break;
                case "animal_boarding": break;
                case "stripclub": break;
                case "apartments": break;
                case "bail_bonds": break;
                case "trade_school": break;
                case "childcare": break;
                case "coworking_space": break;
                case "alternative_medicine": break;
                case "tutor": break;
                case "pet_grooming": break;
                case "art_classes": break;
                case "auto_club": break;
                case "Tailor": break;
                case "dance_studio": break;
                case "events_venue": break;
                case "preschool": break;
                case "taxi": break;
                case "art_studio": break;
                case "dog_care": break;
                case "dog_grooming": break;
                case "lockers": break;
                case "gym": break;
                case "compressed_air": break;
                case "public_bookcase": break;
                case "reuse": break;
                case "medispa": break;
                case "lounge": break;
                case "table": break;
                case "bicycle_repair_station": break;
                case "music_school": break;
                case "device_charging_station": break;
                case "makerspace": break;
                case "charging_station": break;
                default:
                    Console.WriteLine(value);
                    break;
            }

            return amenity;
        }
    }

}
