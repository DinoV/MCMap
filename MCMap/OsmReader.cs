using Kaos.Collections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace MinecraftMapper {
    class OsmReader {
        public readonly Dictionary<long, Way> Ways = new Dictionary<long, Way>();
        public readonly Dictionary<string, List<Way>> RoadsByName = new Dictionary<string, List<Way>>();
        public readonly Dictionary<long, Building> Buildings = new Dictionary<long, Building>();
        public readonly HashSet<Barrier> Barriers = new HashSet<Barrier>();
        public readonly Dictionary<string, Building> BuildingsByAddress = new Dictionary<string, Building>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Node>> BusStops = new Dictionary<string, List<Node>>();
        public readonly Dictionary<Node, SignType> Signs = new Dictionary<Node, SignType>();
        public readonly Dictionary<Node, List<Way>> RoadsByNode = new Dictionary<Node, List<Way>>();
        private readonly string _sourceXml;

        public class SignInfo {
            public readonly SignType Sign;
            public readonly List<Way> Ways = new List<Way>();
        }

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
            TrafficSignal,
            Stop,
            TrafficSign
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
            public readonly bool OneWay;

            public Way(string name, Node[] nodes, int? lanes, RoadType roadType, Sidewalk sidewalk, int? layer, CrossingType crossing, Surface surface, bool oneWay) {
                Name = name;
                Nodes = nodes;
                Lanes = lanes;
                RoadType = roadType;
                Sidewalk = sidewalk;
                OneWay = oneWay;
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
            public readonly RoofInfo Roof;

            public Building(string name, string houseNumber, string street, Amenity amenity, Material material, RoofInfo roofInfo, Node[] nodes) {
                Name = name;
                HouseNumber = houseNumber;
                Street = street;
                Amenity = amenity;
                Nodes = nodes;
                Material = material;
                Roof = roofInfo;
            }

            public Building(string name, string houseNumber, string street, Amenity amenity, Material material, double lat, double longitude, RoofInfo roofInfo, Node[] nodes) {
                Name = name;
                HouseNumber = houseNumber;
                Street = street;
                Amenity = amenity;
                Material = material;
                PrimaryLat = lat;
                PrimaryLong = longitude;
                Roof = roofInfo;
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
            var allNodes = new Dictionary<long, Node>();
            RankedDictionary<double, RankedDictionary<double, SeattleNode>> orderedNodes = new RankedDictionary<double, RankedDictionary<double, SeattleNode>>();

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
                        Node newNode;
                        if (tags.houseNumber != null && tags.street != null) {
                            RankedDictionary<double, SeattleNode> longNodes;
                            if (!orderedNodes.TryGetValue(lat, out longNodes)) {
                                longNodes = orderedNodes[lat] = new RankedDictionary<double, SeattleNode>();
                            }
                            allNodes[id] = newNode = longNodes[longitude] = new SeattleNode(lat, longitude, id, tags.name, tags.houseNumber, tags.street, tags.stories);
                        } else {
                            newNode = allNodes[id] = new Node(lat, longitude);
                        }
                        
                        if (tags.crossing == CrossingType.Zebra) {
                            var zebra = Ways[id] = new Way(
                                tags.name,
                                new[] { newNode },
                                tags.lanes,
                                tags.roadType,
                                tags.sidewalk,
                                tags.layer,
                                tags.crossing,
                                tags.surface,
                                tags.oneWay
                            );
                        } else if (tags.roadType == RoadType.BusStop) {
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
                                busNodes.Add(newNode);
                            }
                        } else if (tags.signType != SignType.None) {
                            Signs[newNode] = tags.signType;
                        } 
                        break;
                    case "way":
                        List<Node> nodes = new List<Node>();
                        foreach (var node in data.Descendants()) {
                            switch (node.Name.LocalName) {
                                case "nd":
                                    nodes.Add(allNodes[Convert.ToInt64(node.Attribute("ref").Value)]);
                                    break;
                                case "tag":
                                    ReadTag(ref tags, node);
                                    break;
                            }
                        }

                        RoofInfo roof = null;
                        if (tags.roofType != RoofType.None ||
                            tags.roofColor != null) {
                            roof = new RoofInfo(tags.roofType, tags.roofColor, tags.roofDirection, tags.roofHeight, tags.roofMaterial, tags.roofOrientationAcross);
                        }
                        if (tags.building != BuildingType.None) {
                            var buildingObj = Buildings[Convert.ToInt64(data.Attribute("id").Value)] = new Building(
                                tags.name, 
                                tags.houseNumber, 
                                tags.street, 
                                tags.amenity, 
                                tags.material,
                                roof,
                                nodes.ToArray()
                            );
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
                            foreach (var group in orderedNodes.ElementsBetween(minLat, maxLat)) {
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
                                            roof,
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
                                tags.surface,
                                tags.oneWay
                            );
                            foreach (var point in nodes) {
                                List<Way> ways;
                                if (!RoadsByNode.TryGetValue(point, out ways)) {
                                    RoadsByNode[point] = ways = new List<Way>(1);
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
                            Barriers.Add(new Barrier(nodes.ToArray(), tags.barrier, tags.wall));
                        }
                        break;
                }
            }
        }

        public class RoofInfo {
            public readonly RoofType Type;
            public readonly int? Direction;
            public readonly Color? Color;
            public readonly double? Height;
            public readonly Material? Material;
            public readonly bool OrientationAcross;

            public RoofInfo(RoofType roofType, Color? roofColor, int? roofDirection, double? roofHeight, Material? roofMaterial, bool roofOrientationAcross) {
                Type = roofType;
                Color = roofColor;
                Direction = roofDirection;
                Height = roofHeight;
                Material = roofMaterial;
                OrientationAcross = roofOrientationAcross;
            }
        }

        public class Barrier {
            public readonly Node[] Nodes;
            public readonly BarrierKind Kind;
            public readonly Wall Wall;

            public Barrier(Node[] nodes, BarrierKind kind, Wall wall) {
                Nodes = nodes;
                Kind = kind;
                Wall = wall;
            }
        }

        public enum Wall {
            None,
            DryStone,
            Brick,
            Flint,
            NoiseBarrier,
            JerseyBarrier,
            Gabion,
            Seawall,
            FloodWall,
            CastleWall
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
            public bool oneWay;
            public RoofType roofType;
            public double? roofHeight;
            internal Color? roofColor;
            internal bool roofOrientationAcross;
            internal int? roofDirection;
            internal Material? roofMaterial;
            public Wall wall;
            internal Parking parking;
        }

        public enum Parking {
            None,
            Surface,
            GarageBoxes,
            CarPorts,
            Sheds,
            RoofTop,
            Underground,
            MultiStory
        }

        public enum RoofType {
            None,
            Flat,
            Skillion,
            Gabled,
            HalfHipped,
            Hipped,
            Pyramidal,
            Gambrel,
            Mansard,
            Dome,
            Onion,
            Round,
            Saltbox
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
                case "wall":
                    switch(value) {
                        case "dry_stone": tagInfo.wall = Wall.DryStone;break;
                        case "brick": tagInfo.wall = Wall.Brick; break;
                        case "flint": tagInfo.wall = Wall.Flint; break;
                        case "noise_barrier": tagInfo.wall = Wall.NoiseBarrier; break;
                        case "jersey_barrier": tagInfo.wall = Wall.JerseyBarrier; break;
                        case "gabion": tagInfo.wall = Wall.Gabion; break;
                        case "seawall": tagInfo.wall = Wall.Seawall; break;
                        case "flood_wall": tagInfo.wall = Wall.FloodWall; break;
                        case "castle_wall": tagInfo.wall = Wall.CastleWall; break;
                    }
                    break;
                case "roof:shape":
                    switch (value) {
                        case "flat": tagInfo.roofType = RoofType.Flat; break;
                        case "skillion": tagInfo.roofType = RoofType.Skillion; break;
                        case "gabled": tagInfo.roofType = RoofType.Gabled; break;
                        case "half-hipped": tagInfo.roofType = RoofType.HalfHipped; break;
                        case "hipped": tagInfo.roofType = RoofType.Hipped; break;
                        case "pyramidal": tagInfo.roofType = RoofType.Pyramidal; break;
                        case "gambrel": tagInfo.roofType = RoofType.Gambrel; break;
                        case "mansard": tagInfo.roofType = RoofType.Mansard; break;
                        case "dome": tagInfo.roofType = RoofType.Dome; break;
                        case "onion": tagInfo.roofType = RoofType.Onion; break;
                        case "round": tagInfo.roofType = RoofType.Round; break;
                        case "saltbox": tagInfo.roofType = RoofType.Saltbox; break;
                    }
                    break;
                case "roof:height":
                    double roofHeight;
                    if (double.TryParse(value, out roofHeight)) {
                        tagInfo.roofHeight = roofHeight;
                    }
                    break;
                case "roof:colour":
                    tagInfo.roofColor = ParseColor(value);
                    break;
                case "roof:orientation":
                    if(value == "across") {
                        tagInfo.roofOrientationAcross = true;
                    }
                    break;
                case "roof:material":
                    tagInfo.roofMaterial = ReadMaterial(value);
                    break;
                case "roof:direction":
                    tagInfo.roofDirection = ParseDirection(value);
                    break;
                case "oneway":
                    if (value == "yes") {
                        tagInfo.oneWay = true;
                    }
                    break;
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
                        case "unpaved": tagInfo.surface = Surface.Unpaved; break;
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
                case "parking":
                    tagInfo.parking = ReadParking(value);
                    break;
                case "amenity":
                    tagInfo.amenity = ReadAmenity(value);
                    break;
                case "layer":
                    tagInfo.layer = Int32.Parse(value);
                    break;
                case "building:part":
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
                        case "traffic_signals": tagInfo.signType = SignType.TrafficSignal; break;
                        case "crossing": tagInfo.roadType = RoadType.Crossing; break;
                        case "stop": tagInfo.signType = SignType.Stop; break;
                        case "turning_circle": break;
                        case "elevator": break;
                        case "give_way": break;
                        case "turning_loop": break;
                        case "mini_roundabout": break;
                        case "passing_place":
                            break;
                        case "street_lamp": tagInfo.signType = SignType.StreetLamp; break;
                        case "traffic_sign": tagInfo.signType = SignType.TrafficSign; break;
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

        private static Parking ReadParking(string value) {
            switch (value) {
                case "surface": return Parking.Surface;
                case "multi-storey": return Parking.MultiStory;
                case "underground": return Parking.Underground;
                case "rooftop": return Parking.RoofTop;
                case "sheds": return Parking.Sheds;
                case "carports": return Parking.CarPorts;
                case "garage_boxes": return Parking.GarageBoxes;
            }
            return Parking.None;
        }

        private static int? ParseDirection(string value) {
            int res;
            if (int.TryParse(value, out res)) {
                return res;
            }
            switch (value) {
                case "N": return 0;
                case "NNE": return 22;
                case "NE": return 45;
                case "ENE": return 67;
                case "E": return 90;
                case "ESE": return 122;
                case "SE": return 135;
                case "SSE": return 157;
                case "S": return 180;
                case "SSW": return 202;
                case "SW": return 225;
                case "WSW": return 247;
                case "W": return 270;
                case "WNW": return 292;
                case "NW": return 315;
                case "NNW": return 337;
            }
            return null;
        }

        public struct Color {
            public readonly int R, G, B;

            public Color(int r, int g, int b) {
                R = r;
                G = g;
                B = b;
            }
        }

        private static Color? ParseColor(string value) {
            if (value.StartsWith("#")) {
                int r, g, b;
                if (value.Length == 4) {
                    if (Int32.TryParse(value.Substring(1, 1), NumberStyles.AllowHexSpecifier, null, out r) &&
                        Int32.TryParse(value.Substring(2, 1), NumberStyles.AllowHexSpecifier, null, out g) &&
                        Int32.TryParse(value.Substring(3, 1), NumberStyles.AllowHexSpecifier, null, out b)) {
                        return new Color(r * 16 + r, g * 16 + g, b * 16 + b);
                    }
                } else if (value.Length == 7) {
                    if (Int32.TryParse(value.Substring(1, 2), NumberStyles.AllowHexSpecifier, null, out r) &&
                        Int32.TryParse(value.Substring(3, 2), NumberStyles.AllowHexSpecifier, null, out g) &&
                        Int32.TryParse(value.Substring(5, 2), NumberStyles.AllowHexSpecifier, null, out b)) {
                        return new Color(r, g, b);
                    }
                }
            }
            switch (value) {
                case "black": return new Color(0, 0, 0);
                case "gray":
                case "grey": return new Color(0x80, 0x80, 0x80);
                case "maroon": return new Color(0x80, 0, 0);
                case "olive": return new Color(0x80, 0x80, 0);
                case "green":  return new Color(0, 0x80, 0);
                case "teal": return new Color(0, 0x80, 0x80);
                case "navy": return new Color(0, 0, 0x80);
                case "purple":  return new Color(0x80, 0, 0x80);
                case "white": return new Color(0xff, 0xff, 0xff);
                case "silver":  return new Color(0xc0, 0xc0, 0xc0);
                case "red": return new Color(0xff, 0, 0);
                case "yellow": return new Color(0xff, 0xff, 0);
                case "lime": return new Color(0, 0xff, 0);
                case "aqua": return new Color(0, 0xff, 0xff);
                case "blue": return new Color(0, 0, 0xff);
                case "fuchsia":
                case "magenta":
                    return new Color(0xff, 0, 0xff);
            }


            return null;
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
            Brick,
            Bamboo,
            PalmLeaves,
            Thatch,
            TarPaper,
            Stone,
            RoofTiles,
            Plants,
            Metal,
            Gravel,
            Grass,
            Glass,
            Asphalt,
            Plastic,
            Copper,
            Slate,
            Wood,
            Concrete
        }

        private static Material ReadMaterial(string value) {
            switch (value) {
                case "sandstone": return Material.Sandstone;
                case "brick": return Material.Brick;
                case "concrete": return Material.Concrete;
                case "copper": return Material.Copper;
                case "plastic": return Material.Plastic;
                case "asphalt": return Material.Asphalt;
                case "glass": return Material.Glass;
                case "grass": return Material.Grass;
                case "gravel": return Material.Gravel;
                case "metal": return Material.Metal;
                case "plants": return Material.Plants;
                case "roof_tiles": return Material.RoofTiles;
                case "slate": return Material.Slate;
                case "stone": return Material.Stone;
                case "tar_paper": return Material.TarPaper;
                case "thatch": return Material.Thatch;
                case "wood": return Material.Wood;
                case "palm_leaves": return Material.PalmLeaves;
                case "bamboo": return Material.Bamboo;
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
