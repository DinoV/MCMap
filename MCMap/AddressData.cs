using CsvHelper;
using Substrate;
using Substrate.TileEntities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static MinecraftMapper.OsmReader;

namespace MinecraftMapper {
    partial class Mapper {
        private void ReadStories(CsvReader residential, string storiesField) {
            residential.Read();
            residential.ReadHeader();
            List<string> address = new List<string>();
            while (residential.Read()) {
                var buildingNo = residential.GetField("BuildingNumber");
                if (!String.IsNullOrWhiteSpace(buildingNo)) {
                    address.Add(buildingNo.Trim());
                }
                var prefix = residential.GetField("DirectionPrefix");
                if (!String.IsNullOrWhiteSpace(prefix)) {
                    prefix = FixSuffix(prefix);
                    address.Add(prefix.Trim());
                }
                var streetName = residential.GetField("StreetName");
                if (!String.IsNullOrWhiteSpace(streetName)) {
                    address.Add(streetName.Trim());
                }
                var streetType = residential.GetField("StreetType");
                switch (streetType.Trim().ToUpper()) {
                    case "AVE": streetType = "Avenue"; break;
                    case "STR":
                    case "ST": streetType = "Street"; break;
                    case "PL": streetType = "Place"; break;
                    case "DR": streetType = "Drive"; break;
                    case "HWY": streetType = "Highway"; break;
                    case "CT": streetType = "Court"; break;
                    case "WAY": streetType = "Way"; break;
                    case "LN": streetType = "Lane"; break;
                    case "BLVD": streetType = "Boulevard"; break;
                    case "TER": streetType = "Terrace"; break;
                    case "RD": streetType = "Road"; break;
                    case "LOOP": streetType = "Loop"; break;
                    case "CIR": streetType = "Circle"; break;
                    case "TRL": streetType = "Trail"; break;
                    case "PW":
                    case "PKWY": streetType = "Parkway"; break;
                    case "WALK": streetType = "Walk"; break;
                    case "PT": streetType = "Point"; break;
                    case "EXT": streetType = "Extension"; break;
                    case "": break;
                    default:
                        break;
                }
                if (!String.IsNullOrWhiteSpace(streetType)) {
                    address.Add(streetType.Trim());
                }
                var suffix = residential.GetField("DirectionSuffix").Trim();
                if (!string.IsNullOrWhiteSpace(suffix)) {
                    suffix = FixSuffix(suffix);
                    address.Add(suffix);
                }
                var fullAddress = string.Join(" ", address);
                OsmReader.Building building;
                if (_reader.BuildingsByAddress.TryGetValue(fullAddress, out building)) {
                    var storiesStr = residential.GetField(storiesField);
                    float stories;
                    if (float.TryParse(storiesStr, out stories)) {
                        building.Stories = stories;
                    }
                }

                address.Clear();
            }
        }

        private static string FixSuffix(string prefix) {
            switch (prefix.Trim().ToUpper()) {
                case "S": prefix = "South"; break;
                case "N": prefix = "North"; break;
                case "NE": prefix = "Northeast"; break;
                case "NW": prefix = "Northwest"; break;
                case "SE": prefix = "Southeast"; break;
                case "SW": prefix = "Southwest"; break;
                case "E": prefix = "East"; break;
                case "W": prefix = "West"; break;
            }

            return prefix;
        }

    }
}
