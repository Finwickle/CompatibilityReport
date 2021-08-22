﻿using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using CompatibilityReport.Util;


namespace CompatibilityReport.DataTypes
{
    // Needs to be public for XML serialization
    [Serializable] public class Mod
    {
        // Steam ID and name
        public ulong SteamID { get; private set; }

        public string Name { get; private set; }

        // Date the mod was published and last updated on the Steam Workshop
        public DateTime Published { get; private set; }

        public DateTime Updated { get; private set; }

        // Author Profile ID and Author Custom URL; only one is needed to identify the author; ID is more reliable
        public ulong AuthorID { get; private set; }

        public string AuthorURL { get; private set; }

        // An archive page of the Steam Workshop page, for mods that were removed from the Steam Workshop
        public string ArchiveURL { get; private set; }

        // Public location of the source
        public string SourceURL { get; private set; }

        // Game version this mod is compatible with; 'Version' is not serializable, so we use a string; convert to Version when needed
        public string CompatibleGameVersionString { get; private set; }

        // Required DLCs
        public List<Enums.DLC> RequiredDLC { get; private set; } = new List<Enums.DLC>();

        // Required mods for this mod (all are required); this is the only list that allows groups, meaning one (not all) of the mods in the group is required
        [XmlArrayItem("SteamID")] public List<ulong> RequiredMods { get; private set; } = new List<ulong>();

        // Successors of this mod
        [XmlArrayItem("SteamID")] public List<ulong> Successors { get; private set; } = new List<ulong>();

        // Alternatives for this mod
        [XmlArrayItem("SteamID")] public List<ulong> Alternatives { get; private set; } = new List<ulong>();

        // Recommended mods to use with this mod
        [XmlArrayItem("SteamID")] public List<ulong> Recommendations { get; private set; } = new List<ulong>();

        // Mod stability
        public Enums.ModStability Stability;

        public string StabilityNote { get; private set; }


        // Statuses for this mod
        public List<Enums.ModStatus> Statuses { get; private set; } = new List<Enums.ModStatus>();

        // Generic note about this mod
        public string GenericNote { get; private set; }

        // Exclusions
        public bool ExclusionForSourceURL { get; private set; }

        public bool ExclusionForGameVersion { get; private set; }

        public bool ExclusionForNoDescription { get; private set; }

        public List<Enums.DLC> ExclusionForRequiredDLC { get; private set; } = new List<Enums.DLC>();

        [XmlArrayItem("SteamID")] public List<ulong> ExclusionForRequiredMods { get; private set; } = new List<ulong>();

        // Date this mod was last manually and automatically reviewed for changes in information and compatibilities
        public DateTime ReviewDate { get; private set; }

        public DateTime AutoReviewDate { get; private set; }

        // Change notes, automatically filled by the updater; not displayed in report or log, but visible in the catalog
        [XmlArrayItem("ChangeNote")] public List<string> ChangeNotes { get; private set; } = new List<string>();

        // Indicator to see if this was updated this session
        [XmlIgnore] internal bool UpdatedThisSession { get; private set; }


        // Default constructor
        public Mod()
        {
            // Nothing to do here
        }


        // Constructor with 4 or 5 parameters
        internal Mod(ulong steamID, string name, ulong authorID, string authorURL, bool incompatible = false)
        {
            SteamID = steamID;

            Name = name ?? "";

            AuthorID = authorID;

            AuthorURL = authorURL ?? "";

            if (incompatible)
            {
                Stability = Enums.ModStability.IncompatibleAccordingToWorkshop;
            }
        }


        // Update a mod with new info; all fields can be updated except Steam ID; all fields are optional, only supplied fields are updated
        internal void Update(string name = null,
                             DateTime? published = null,
                             DateTime? updated = null,
                             ulong authorID = 0,
                             string authorURL = null,
                             string archiveURL = null,
                             string sourceURL = null,
                             string compatibleGameVersionString = null,
                             List<Enums.DLC> requiredDLC = null,
                             List<ulong> requiredMods = null,
                             List<ulong> successors = null,
                             List<ulong> alternatives = null,
                             List<ulong> recommendations = null,
                             Enums.ModStability stability = default,
                             string stabilityNote = null,
                             List<Enums.ModStatus> statuses = null,
                             string genericNote = null,
                             bool? exclusionForSourceURL = null,
                             bool? exclusionForGameVersion = null,
                             bool? exclusionForNoDescription = null,
                             List<Enums.DLC> exclusionForRequiredDLC = null,
                             List<ulong> exclusionForRequiredMods = null,
                             DateTime? reviewDate = null,
                             DateTime? autoReviewDate = null,
                             List<string> replacementChangeNotes = null,
                             string extraChangeNote = null)
        {
            // Only update supplied fields, so ignore every null value; make sure strings are set to empty strings/lists instead of null
            Name = name ?? Name ?? "";

            Published = published ?? Published;

            // If the updated date is older than published, set it to published
            Updated = updated ?? Updated;
            Updated = Updated < Published ? Published : Updated;

            AuthorID = authorID == 0 ? AuthorID : authorID;

            AuthorURL = authorURL ?? AuthorURL ?? "";

            ArchiveURL = archiveURL ?? ArchiveURL ?? "";

            SourceURL = sourceURL ?? SourceURL ?? "";

            // If the game version string is null, set it to the unknown game version
            CompatibleGameVersionString = compatibleGameVersionString ?? CompatibleGameVersionString ?? GameVersion.Unknown.ToString();

            RequiredDLC = requiredDLC ?? RequiredDLC ?? new List<Enums.DLC>();

            RequiredMods = requiredMods ?? RequiredMods ?? new List<ulong>();

            Successors = successors ?? Successors ?? new List<ulong>();

            Alternatives = alternatives ?? Alternatives ?? new List<ulong>();

            Recommendations = recommendations ?? Recommendations ?? new List<ulong>();

            Stability = stability == default ? Stability : stability;

            StabilityNote = stabilityNote ?? StabilityNote ?? "";

            Statuses = statuses ?? Statuses ?? new List<Enums.ModStatus>();

            GenericNote = genericNote ?? GenericNote ?? "";

            ExclusionForSourceURL = exclusionForSourceURL ?? ExclusionForSourceURL;

            ExclusionForGameVersion = exclusionForGameVersion ?? ExclusionForGameVersion;

            ExclusionForNoDescription = exclusionForNoDescription ?? ExclusionForNoDescription;

            ExclusionForRequiredDLC = exclusionForRequiredDLC ?? ExclusionForRequiredDLC ?? new List<Enums.DLC>();

            ExclusionForRequiredMods = exclusionForRequiredMods ?? ExclusionForRequiredMods ?? new List<ulong>();

            ReviewDate = reviewDate ?? ReviewDate;

            AutoReviewDate = autoReviewDate ?? AutoReviewDate;

            // Replace the change notes and/or add a note
            ChangeNotes = replacementChangeNotes ?? ChangeNotes ?? new List<string>();

            if (!string.IsNullOrEmpty(extraChangeNote))
            {
                ChangeNotes.Add(extraChangeNote);
            }

            // Set updated this session to true, independent of an actual value update
            UpdatedThisSession = true;
        }


        // Return a max length, formatted string with the Steam ID and name
        internal string ToString(bool nameFirst = false,
                                 bool showFakeID = true,
                                 bool cutOff = false)
        {
            string id;

            if (SteamID > ModSettings.highestFakeID)
            {
                // Workshop mod
                id = $"[Steam ID { SteamID, 10 }]";
            }
            else if ((SteamID >= ModSettings.lowestLocalModID) && (SteamID <= ModSettings.highestLocalModID))
            {
                // Local mod
                id = "[local mod" + (showFakeID ? " " + SteamID.ToString() : "") + "]";
            }
            else
            {
                // Builtin mod
                id = "[builtin mod" + (showFakeID ? " " + SteamID.ToString() : "") + "]";
            }

            int maxNameLength = ModSettings.maxReportWidth - 1 - id.Length;

            // Cut off the name to max. length, if the cutOff parameter is true
            string name = (Name.Length <= maxNameLength) || !cutOff ? Name : Name.Substring(0, maxNameLength - 3) + "...";

            if (nameFirst)
            {
                return name + " " + id;
            }
            else
            {
                return id + " " + name;
            }
        }


        // Add an exclusion for a required DLC if it doesn't exist yet
        internal void AddExclusionForRequiredDLC(Enums.DLC requiredDLC)
        {
            if (!ExclusionForRequiredDLC.Contains(requiredDLC))
            {
                ExclusionForRequiredDLC.Add(requiredDLC);
            }
        }


        // Add an exclusion for a required mod if it doesn't exist yet
        internal void AddExclusionForRequiredMods(ulong requiredMod)
        {
            if (!ExclusionForRequiredMods.Contains(requiredMod))
            {
                ExclusionForRequiredMods.Add(requiredMod);
            }
        }
    }
}
