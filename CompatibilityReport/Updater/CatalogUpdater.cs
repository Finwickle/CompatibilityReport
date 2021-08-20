﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CompatibilityReport.DataTypes;
using CompatibilityReport.Util;


// CatalogUpdater uses information gathered by WebCrawler and FileImporter to update the catalog and save this as a new version, with auto generated change notes.


namespace CompatibilityReport.Updater
{
    internal static class CatalogUpdater    // [Todo 0.3] move some actions from FileImporter to here; move some actions between here and Catalog
    {
        // Did we run already this session (successful or not)
        private static bool hasRun;

        // Date of the catalog creation, always 'today'. This is used in mod/author change notes.
        private static string catalogDateString;

        // Date of the review update for affected mods. This can be set in the CSV file and is used in mod review dates.
        private static DateTime reviewDate;

        // Stringbuilder to collect new required assets we found
        private static StringBuilder UnknownRequiredAssets;

        // Stringbuilder to gather the combined CSVs, to be saved with the new catalog
        internal static StringBuilder CSVCombined;

        // Change notes, separate parts and combined
        private static StringBuilder changeNotesCatalog;
        private static StringBuilder changeNotesNewMods;
        private static StringBuilder changeNotesNewGroups;
        private static StringBuilder changeNotesNewCompatibilities;
        private static StringBuilder changeNotesNewAuthors;
        private static Dictionary<ulong, string> changeNotesUpdatedMods;
        private static Dictionary<ulong, string> changeNotesUpdatedAuthorsByID;
        private static Dictionary<string, string> changeNotesUpdatedAuthorsByURL;
        private static StringBuilder changeNotesRemovedMods;
        private static StringBuilder changeNotesRemovedGroups;
        private static StringBuilder changeNotesRemovedCompatibilities;
        private static string changeNotes;

        
        // Update the active catalog with the found information; returns the partial path of the new catalog
        internal static void Start()
        {
            // Exit if we ran already, the updater is not enabled in settings, or if we don't have and can't get an active catalog
            if (hasRun || !ModSettings.UpdaterEnabled || !ActiveCatalog.Init())
            {
                return;
            }

            hasRun = true;

            Logger.Log("Catalog Updater started. See separate logfile for details.");

            Logger.UpdaterLog($"Catalog Updater started. { ModSettings.modName } version { ModSettings.fullVersion }. " + 
                $"Game version { GameVersion.Formatted(GameVersion.Current) }. Current catalog version { ActiveCatalog.Instance.VersionString() }.");

            Init();

            if (ModSettings.WebCrawlerEnabled)
            {
                WebCrawler.Start();
            }
            
            // Run the FileImporter for catalog version 3 and higher   [Todo 0.4] Remove requirement
            if (ActiveCatalog.Instance.Version > 2)
            {
                FileImporter.Start();
            }

            // Retire authors that are now eligible
            RetireEligibleAuthors();

            // Log a CSV action for required assets that are missing in the catalog
            if (UnknownRequiredAssets.Length > 0)
            {
                Logger.UpdaterLog("CSV action for adding assets to the catalog (after verification): Add_RequiredAssets" + UnknownRequiredAssets.ToString());
            }

            // Only continue with catalog update if we found any changes to update the catalog (ignoring the pure catalog changes)
            if (changeNotesNewMods.Length + changeNotesNewGroups.Length + changeNotesNewCompatibilities.Length + changeNotesNewAuthors.Length + 
                changeNotesUpdatedMods.Count + changeNotesUpdatedAuthorsByID.Count + changeNotesUpdatedAuthorsByURL.Count + 
                changeNotesRemovedMods.Length + changeNotesRemovedGroups.Length + changeNotesRemovedCompatibilities.Length == 0)
            {
                Logger.UpdaterLog("No changes or new additions found. No new catalog created.");
            }
            else
            {
                UpdateChangeNotes();

                string partialPath = Path.Combine(ModSettings.updaterPath, $"{ ModSettings.internalName }Catalog_v{ ActiveCatalog.Instance.VersionString() }");

                // Save the new catalog
                if (ActiveCatalog.Instance.Save(partialPath + ".xml"))
                {
                    // Save change notes, in the same folder as the new catalog
                    Toolkit.SaveToFile(changeNotes.ToString(), partialPath + "_ChangeNotes.txt");

                    // Save the combined CSVs, in the same folder as the new catalog
                    Toolkit.SaveToFile(CSVCombined.ToString(), partialPath + "_Imports.csv.txt");

                    Logger.UpdaterLog($"New catalog { ActiveCatalog.Instance.VersionString() } created and change notes saved.");

                    // Copy the updater logfile to the same folder as the new catalog
                    Toolkit.CopyFile(ModSettings.updaterLogfileFullPath, partialPath + "_Updater.log");
                }
                else
                {
                    Logger.UpdaterLog("Could not save the new catalog. All updates were lost.", Logger.error);
                }
            }

            // Empty the dictionaries and change notes to free memory
            Init();

            // Close and reopen the active catalog, to get rid of loose ends, if any
            Logger.UpdaterLog("Closing and reopening the active catalog.", duplicateToRegularLog: true);

            ActiveCatalog.Close();

            ActiveCatalog.Init();

            // Run the DataDumper
            DataDumper.Start();

            Logger.UpdaterLog("Catalog Updater has finished.", extraLine: true, duplicateToRegularLog: true);
        }


        // Get all variables and the catalog ready for updating
        private static void Init()
        {
            UnknownRequiredAssets = new StringBuilder();

            changeNotesCatalog = new StringBuilder();
            changeNotesNewMods = new StringBuilder();
            changeNotesNewGroups = new StringBuilder();
            changeNotesNewCompatibilities = new StringBuilder();
            changeNotesNewAuthors = new StringBuilder();
            changeNotesUpdatedMods = new Dictionary<ulong, string>();
            changeNotesUpdatedAuthorsByID = new Dictionary<ulong, string>();
            changeNotesUpdatedAuthorsByURL = new Dictionary<string, string>();
            changeNotesRemovedMods = new StringBuilder();
            changeNotesRemovedGroups = new StringBuilder();
            changeNotesRemovedCompatibilities = new StringBuilder();
            changeNotes = "";

            CSVCombined = new StringBuilder();

            // Increase the catalog version and update date
            ActiveCatalog.Instance.NewVersion(DateTime.Now);

            catalogDateString = Toolkit.DateString(ActiveCatalog.Instance.UpdateDate.Date);

            // Set a special catalog note for version 2, and reset it again for version 3
            if (ActiveCatalog.Instance.Version == 2)
            {
                SetNote(ModSettings.secondCatalogNote);
            }
            else if (ActiveCatalog.Instance.Version == 3)
            {
                SetNote("");
            }
        }


        // Update change notes in the mod and author change note fields, and combine the change notes for the change notes file
        private static void UpdateChangeNotes()
        {
            StringBuilder changeNotesUpdatedModsCombined = new StringBuilder();

            StringBuilder changeNotesUpdatedAuthorsCombined = new StringBuilder();

            foreach (KeyValuePair<ulong, string> modNotes in changeNotesUpdatedMods)
            {
                if (!string.IsNullOrEmpty(modNotes.Value))
                {
                    string cleanedChangeNote = modNotes.Value.Substring(2);

                    ActiveCatalog.Instance.ModDictionary[modNotes.Key].Update(extraChangeNote: $"{ catalogDateString }: { cleanedChangeNote }");

                    changeNotesUpdatedModsCombined.AppendLine($"Mod { ActiveCatalog.Instance.ModDictionary[modNotes.Key].ToString(cutOff: false) }: " +
                        $"{ cleanedChangeNote }");
                }
            }

            foreach (KeyValuePair<ulong, string> authorNotes in changeNotesUpdatedAuthorsByID)
            {
                string cleanedChangeNote = authorNotes.Value.Substring(2);

                ActiveCatalog.Instance.AuthorIDDictionary[authorNotes.Key].Update(extraChangeNote: $"{ catalogDateString }: { cleanedChangeNote }");

                changeNotesUpdatedAuthorsCombined.AppendLine($"Author { ActiveCatalog.Instance.AuthorIDDictionary[authorNotes.Key].ToString() }: " +
                    $"{ cleanedChangeNote }");
            }

            foreach (KeyValuePair<string, string> authorNotes in changeNotesUpdatedAuthorsByURL)
            {
                string cleanedChangeNote = authorNotes.Value.Substring(2);

                ActiveCatalog.Instance.AuthorURLDictionary[authorNotes.Key].Update(extraChangeNote: $"{ catalogDateString }: { cleanedChangeNote }");

                changeNotesUpdatedAuthorsCombined.AppendLine($"Author { ActiveCatalog.Instance.AuthorURLDictionary[authorNotes.Key].ToString() }: " +
                    $"{ cleanedChangeNote }");
            }

            // Combine the total change notes
            changeNotes = $"Change Notes for Catalog { ActiveCatalog.Instance.VersionString() }\n" +
                "-------------------------------\n" +
                $"{ ActiveCatalog.Instance.UpdateDate:D}, { ActiveCatalog.Instance.UpdateDate:t}\n" +
                "These change notes were automatically created by the updater process.\n" +
                "\n" +
                "\n" +
                (changeNotesCatalog.Length == 0 ? "" :
                    "*** CATALOG CHANGES: ***\n" +
                    changeNotesCatalog.ToString() +
                    "\n") +
                (changeNotesNewMods.Length + changeNotesNewGroups.Length + changeNotesNewAuthors.Length == 0 ? "" :
                    "*** ADDED: ***\n" +
                    changeNotesNewMods.ToString() +
                    changeNotesNewGroups.ToString() +
                    changeNotesNewCompatibilities.ToString() +
                    changeNotesNewAuthors.ToString() +
                    "\n") +
                (changeNotesUpdatedMods.Count + changeNotesUpdatedAuthorsByID.Count + changeNotesUpdatedAuthorsByURL.Count == 0 ? "" :
                    "*** UPDATED: ***\n" +
                    changeNotesUpdatedModsCombined.ToString() +
                    changeNotesUpdatedAuthorsCombined.ToString() +
                    "\n") +
                (changeNotesRemovedMods.Length + changeNotesRemovedGroups.Length + changeNotesRemovedCompatibilities.Length == 0 ? "" :
                    "*** REMOVED: ***\n" +
                    changeNotesRemovedMods.ToString() +
                    changeNotesRemovedGroups.ToString() +
                    changeNotesRemovedCompatibilities.ToString());
        }


        // Set the review date
        internal static string SetReviewDate(DateTime newDate)
        {
            if (newDate == default)
            {
                return "Invalid Date.";
            }

            reviewDate = newDate;

            return "";
        }


        // Set a new note for the catalog
        internal static void SetNote(string newCatalogNote)
        {
            string change = string.IsNullOrEmpty(newCatalogNote) ? "removed" : string.IsNullOrEmpty(ActiveCatalog.Instance.Note) ? "added" : "changed";

            ActiveCatalog.Instance.Update(note: newCatalogNote);

            AddCatalogChangeNote($"Catalog note { change }.");
        }


        // Set a new header text for the catalog
        internal static void SetHeaderText(string text)
        {
            string change = string.IsNullOrEmpty(text) ? "removed" : string.IsNullOrEmpty(ActiveCatalog.Instance.ReportHeaderText) ? "added" : "changed";

            ActiveCatalog.Instance.Update(reportHeaderText: text);

            AddCatalogChangeNote($"Catalog header text { change }.");
        }


        // Set a new footer text for the catalog
        internal static void SetFooterText(string text)
        {
            string change = string.IsNullOrEmpty(text) ? "removed" : string.IsNullOrEmpty(ActiveCatalog.Instance.ReportFooterText) ? "added" : "changed";
            
            ActiveCatalog.Instance.Update(reportFooterText: text);

            AddCatalogChangeNote($"Catalog footer text { change }.");
        }


        // Add or get a mod. When adding, a mod name, incompatible stability and/or unlisted/removed status can be supplied. On existing mods this is ignored.
        // A review date is not set, that is only done on UpdateMod()
        internal static Mod GetOrAddMod(ulong steamID,
                                        string name,
                                        bool incompatible = false,
                                        bool unlisted = false,
                                        bool removed = false)
        {
            Mod catalogMod;

            // Get the mod from the catalog, or add a new one
            if (ActiveCatalog.Instance.ModDictionary.ContainsKey(steamID))
            {
                // Get the catalog mod and update the name, if needed
                catalogMod = ActiveCatalog.Instance.ModDictionary[steamID];
            }
            else
            {
                // Add a new mod
                catalogMod = ActiveCatalog.Instance.AddOrUpdateMod(steamID, name);

                string modType = "Mod";

                // Add incompatible status if needed
                if (incompatible)
                {
                    catalogMod.Update(stability: Enums.ModStability.IncompatibleAccordingToWorkshop);

                    modType = "Incompatible mod";
                }

                // Add removed or unlisted status if needed
                if (unlisted || removed)
                {
                    catalogMod.Statuses.Add(unlisted ? Enums.ModStatus.UnlistedInWorkshop : Enums.ModStatus.RemovedFromWorkshop);

                    modType = (unlisted ? "Unlisted " : "Removed ") + modType.ToLower();
                }

                // Set mod change note
                catalogMod.Update(extraChangeNote: $"{ catalogDateString }: added");

                changeNotesNewMods.AppendLine($"{ modType } added: { catalogMod.ToString(cutOff: false) }");
            }

            return catalogMod;
        }


        // Update a mod with newly found information, including exclusions  [Todo 0.3] Needs more logic for authorID/authorURL, all lists, ... (combine with FileImporter)
        internal static void UpdateMod(Mod catalogMod,
                                       string name = null,
                                       DateTime published = default,
                                       DateTime updated = default,
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
                                       bool alwaysUpdateReviewDate = false,
                                       bool updatedByWebCrawler = false)
        {
            // Set the change note for all changed values
            string addedChangeNote =
                (name == null || name == catalogMod.Name ? "" : ", mod name changed") +
                (updated == default || updated == catalogMod.Updated ? "" : ", new update") +
                (authorID == 0 || authorID == catalogMod.AuthorID ? "" : ", author ID added") +
                (authorURL == null || authorURL == catalogMod.AuthorURL ? "" : ", author URL") +
                (archiveURL == null || archiveURL == catalogMod.ArchiveURL ? "" : ", archive URL") +
                (sourceURL == null || sourceURL == catalogMod.SourceURL ? "" : ", source URL") +
                (compatibleGameVersionString == null || compatibleGameVersionString == catalogMod.CompatibleGameVersionString ? "" : ", compatible game version") +
                (requiredDLC == null || requiredDLC == catalogMod.RequiredDLC ? "" : ", required DLC") +
                (requiredMods == null || requiredMods == catalogMod.RequiredMods ? "" : ", required mod") +
                (successors == null || successors == catalogMod.Successors ? "" : ", successor mod") +
                (alternatives == null || alternatives == catalogMod.Alternatives ? "" : ", alternative mod") +
                (recommendations == null || recommendations == catalogMod.Recommendations ? "" : ", recommended mod") +
                (stability == default | stability == catalogMod.Stability ? "" : ", stability") +
                (stabilityNote == null || stabilityNote == catalogMod.StabilityNote ? "" : ", mod note") +
                (statuses == null || statuses == catalogMod.Statuses ? "" : ", status") +
                (genericNote == null || genericNote == catalogMod.GenericNote ? "" : ", mod note");

            AddUpdatedModChangeNote(catalogMod, addedChangeNote);

            // Set the update date
            DateTime? modReviewDate = null;

            DateTime? modAutoReviewDate = null;

            if (!string.IsNullOrEmpty(addedChangeNote) || alwaysUpdateReviewDate)
            {
                modReviewDate = !updatedByWebCrawler ? reviewDate : modReviewDate;

                modAutoReviewDate = updatedByWebCrawler ? reviewDate : modAutoReviewDate;
            }

            // [Todo 0.3] Exclusions:
            //      sourceurl (set on add, swap on remove), gameversion (if different than current, remove if higher gameversion set on auto-update and on remove)
            //      nodescription (manually set and unset)

            // Log an empty mod name, the first time it is found. This could be an error, although there is a workshop mod without a name (ofcourse there is)
            if (name == "" && !string.IsNullOrEmpty(catalogMod.Name))
            {
                Logger.UpdaterLog($"Mod name not found: { catalogMod.ToString(cutOff: false) }.", Logger.warning);
            }

            // Update the mod
            catalogMod.Update(name, published, updated, authorID, authorURL, archiveURL, sourceURL, compatibleGameVersionString, requiredDLC, requiredMods,
                successors, alternatives, recommendations, stability, stabilityNote, statuses, genericNote,
                reviewDate: modReviewDate, autoReviewDate: modAutoReviewDate);
        }


        // Add a group
        internal static void AddGroup(string groupName, List<ulong> groupMembers)
        {
            Group newGroup = ActiveCatalog.Instance.AddGroup(groupName, new List<ulong>());

            if (newGroup != null)
            {
                // Add group members separately to get change notes on all group members
                foreach (ulong groupMember in groupMembers)
                {
                    AddGroupMember(newGroup, groupMember);
                }

                changeNotesNewGroups.AppendLine($"Group added: { newGroup.ToString() }");
            }
        }


        // Remove a group
        internal static void RemoveGroup(ulong groupID)
        {
            // Remove the group from all required mod lists
            foreach (Mod catalogMod in ActiveCatalog.Instance.Mods)
            {
                catalogMod.RequiredMods.Remove(groupID);
            }

            Group oldGroup = ActiveCatalog.Instance.GroupDictionary[groupID];

            if (ActiveCatalog.Instance.Groups.Remove(oldGroup))
            {
                ActiveCatalog.Instance.GroupDictionary.Remove(groupID);

                changeNotesRemovedGroups.AppendLine($"Group removed: { oldGroup.ToString() }");

                // Remove group members to get change notes on all former group members
                foreach (ulong groupMember in oldGroup.GroupMembers)
                {
                    AddUpdatedModChangeNote(ActiveCatalog.Instance.ModDictionary[groupMember], $"removed from { oldGroup.ToString() }");
                }
            }
        }


        // Add a group member
        internal static void AddGroupMember(Group group, ulong groupMember)
        {
            group.GroupMembers.Add(groupMember);

            AddUpdatedModChangeNote(ActiveCatalog.Instance.ModDictionary[groupMember], $"added to { group.ToString() }");
        }


        // Remove a group member
        internal static bool RemoveGroupMember(Group group, ulong groupMember)
        {
            if (!group.GroupMembers.Remove(groupMember))
            {
                return false;
            }

            AddUpdatedModChangeNote(ActiveCatalog.Instance.ModDictionary[groupMember], $"removed from { group.ToString() }");

            return true;
        }


        // Add a compatibility
        internal static void AddCompatibility(ulong firstModID, ulong secondModID, Enums.CompatibilityStatus compatibilityStatus, string note)
        {
            Compatibility compatibility = new Compatibility(firstModID, secondModID, compatibilityStatus, note);

            ActiveCatalog.Instance.Compatibilities.Add(compatibility);

            changeNotesNewCompatibilities.AppendLine($"Compatibility added between { firstModID } and { secondModID }: \"{ compatibilityStatus }\", { note }");
        }


        // Remove a compatibility
        internal static bool RemoveCompatibility(ulong firstModID, ulong secondModID, Enums.CompatibilityStatus compatibilityStatus)
        {
            Compatibility catalogCompatibility = ActiveCatalog.Instance.Compatibilities.Find(x => x.SteamID1 == firstModID && x.SteamID2 == secondModID &&
                x.Status == compatibilityStatus);

            if (!ActiveCatalog.Instance.Compatibilities.Remove(catalogCompatibility))
            {
                return false;
            }

            changeNotesRemovedCompatibilities.AppendLine($"Compatibility removed between { firstModID } and { secondModID }: \"{ compatibilityStatus }\"");

            return true;
        }


        // Add or get an author
        internal static Author GetOrAddAuthor(ulong authorID, string authorURL, string authorName)
        {
            Author catalogAuthor = ActiveCatalog.Instance.GetAuthor(authorID, authorURL);

            // Log if the author name is equal to the author ID. Could be an error, although some authors have their ID as name (ofcourse they do)
            if (authorID != 0 && authorName == authorID.ToString() && (catalogAuthor == null || catalogAuthor.Name != authorName))
            {
                Logger.UpdaterLog($"Author found with profile ID as name ({ authorID }). This could be a Steam error.", Logger.warning);
            }

            if (catalogAuthor == null)
            {
                // New author
                catalogAuthor = ActiveCatalog.Instance.AddAuthor(authorID, authorURL, authorName);

                catalogAuthor.Update(extraChangeNote: $"{ catalogDateString }: added");

                changeNotesNewAuthors.AppendLine($"Author added: { catalogAuthor.ToString() }");
            }
            else
            {
                // Existing author. Update the name, if needed.
                NewUpdateAuthor(catalogAuthor, name: authorName);
            }

            return catalogAuthor;
        }


        // Update an author with newly found information, including exclusions
        internal static void NewUpdateAuthor(Author catalogAuthor,
                                             ulong authorID = 0,
                                             string authorURL = null,
                                             string name = null,
                                             DateTime? lastSeen = null,
                                             bool? retired = null,
                                             string extraChangeNote = null,
                                             bool? manuallyUpdated = null)
        {
            // [Todo 0.3] new author ID -> add authorID to all mods from this author; changed author URL -> change authorURL to all mods from this author
            // [Todo 0.3] if lastseen updated, then check retired: if lastseen now less than 12 months ago, retired = false and exclusionforretired = false

            if (lastSeen != null)
            {
                // Set to retired if now past the time for retirement, and set to not retired if not / no longer past the time and no exclusion exists
                if (((DateTime)lastSeen).AddMonths(ModSettings.monthsOfInactivityToRetireAuthor) < DateTime.Today)
                {
                    retired = true;
                }
                else
                {
                    retired = catalogAuthor.ExclusionForRetired;
                }
            }

            if (retired == true && catalogAuthor.LastSeen.AddMonths(ModSettings.monthsOfInactivityToRetireAuthor) >= DateTime.Today)
            {
                catalogAuthor.Update(exclusionForRetired: true);
            }
            else if (retired != null)
            {
                // Remove exclusion for anything other than early retirement.
                catalogAuthor.Update(exclusionForRetired: false);
            }

            // [Todo 0.3] update

            // Update the retired exclusion: only ...
            catalogAuthor.Update(exclusionForRetired:
                catalogAuthor.ExclusionForRetired && catalogAuthor.LastSeen.AddMonths(ModSettings.monthsOfInactivityToRetireAuthor) >= DateTime.Today);
        }


        // Retire authors that are now eligible due to last seen date, and authors that don't have a non-removed mod in the workshop anymore
        private static void RetireEligibleAuthors()
        {
            foreach (Author catalogAuthor in ActiveCatalog.Instance.Authors)
            {
                if (!catalogAuthor.Retired && catalogAuthor.LastSeen.AddMonths(ModSettings.monthsOfInactivityToRetireAuthor) < DateTime.Today)
                {
                    NewUpdateAuthor(catalogAuthor, retired: true);
                }
                else if (!catalogAuthor.Retired && catalogAuthor.ProfileID != 0)
                {
                    if (ActiveCatalog.Instance.Mods.Find(x => x.AuthorID == catalogAuthor.ProfileID) == default)
                    {
                        NewUpdateAuthor(catalogAuthor, retired: true);

                        AddUpdatedAuthorChangeNote(catalogAuthor, "no longer has mods on the workshop");
                    }
                }
                else if (!catalogAuthor.Retired && catalogAuthor.ProfileID == 0)
                {
                    if (ActiveCatalog.Instance.Mods.Find(x => x.AuthorURL == catalogAuthor.CustomURL) == default)
                    {
                        NewUpdateAuthor(catalogAuthor, retired: true);

                        AddUpdatedAuthorChangeNote(catalogAuthor, "no longer has mods on the workshop");
                    }
                }
            }
        }


        // Add a mod status, including exclusions and removing conflicting statuses.
        internal static void AddStatus(Mod catalogMod, Enums.ModStatus status, bool updatedByWebCrawler = false)
        {
            if (status == default || catalogMod.Statuses.Contains(status))
            {
                return;
            }

            catalogMod.Statuses.Add(status);

            AddUpdatedModChangeNote(catalogMod, $"{ status } added");

            // Remove conflicting statuses, and change some exclusions
            if (status == Enums.ModStatus.UnlistedInWorkshop)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.RemovedFromWorkshop);
            }
            else if (status == Enums.ModStatus.RemovedFromWorkshop)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.UnlistedInWorkshop);
                RemoveStatus(catalogMod, Enums.ModStatus.NoCommentSectionOnWorkshop);
                RemoveStatus(catalogMod, Enums.ModStatus.NoDescription);
                catalogMod.Update(exclusionForNoDescription: false);
            }
            else if (status == Enums.ModStatus.NoDescription && updatedByWebCrawler)
            {
                // No exclusion is needed if this status was found by the WebCrawler
                catalogMod.Update(exclusionForNoDescription: true);
            }
            else if (status == Enums.ModStatus.NoLongerNeeded)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.Deprecated);
                RemoveStatus(catalogMod, Enums.ModStatus.Abandoned);
            }
            else if (status == Enums.ModStatus.Deprecated)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.NoLongerNeeded);
                RemoveStatus(catalogMod, Enums.ModStatus.Abandoned);
            }
            else if (status == Enums.ModStatus.Abandoned)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.NoLongerNeeded);
                RemoveStatus(catalogMod, Enums.ModStatus.Deprecated);
            }
            else if (status == Enums.ModStatus.SourceUnavailable)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.SourceBundled);
                RemoveStatus(catalogMod, Enums.ModStatus.SourceNotUpdated);
                RemoveStatus(catalogMod, Enums.ModStatus.SourceObfuscated);

                if (!string.IsNullOrEmpty(catalogMod.SourceURL))
                {
                    UpdateMod(catalogMod, sourceURL: "");

                    catalogMod.Update(exclusionForSourceURL: true);
                }
            }
            else if (status == Enums.ModStatus.SourceBundled || status == Enums.ModStatus.SourceNotUpdated || status == Enums.ModStatus.SourceObfuscated)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.SourceUnavailable);
            }
            else if (status == Enums.ModStatus.MusicCopyrighted)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.MusicCopyrightFree);
                RemoveStatus(catalogMod, Enums.ModStatus.MusicCopyrightUnknown);
            }
            else if (status == Enums.ModStatus.MusicCopyrightFree)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.MusicCopyrighted);
                RemoveStatus(catalogMod, Enums.ModStatus.MusicCopyrightUnknown);
            }
            else if (status == Enums.ModStatus.MusicCopyrightUnknown)
            {
                RemoveStatus(catalogMod, Enums.ModStatus.MusicCopyrighted);
                RemoveStatus(catalogMod, Enums.ModStatus.MusicCopyrightFree);
            }
        }


        // Remove a mod status
        internal static bool RemoveStatus(Mod catalogMod, Enums.ModStatus status, bool updatedByWebCrawler = false)
        {
            bool success = catalogMod.Statuses.Remove(status);

            if (success)
            {
                AddUpdatedModChangeNote(catalogMod, $"{ status } removed");

                // Add or remove exclusion for some statuses
                if (status == Enums.ModStatus.NoDescription && updatedByWebCrawler)
                {
                    // If the status is not removed by the WebCrawler, then: if there was an exclusion, remove it, otherwise add it.
                    catalogMod.Update(exclusionForNoDescription: !catalogMod.ExclusionForNoDescription);
                }
                else if (status == Enums.ModStatus.SourceUnavailable)
                {
                    catalogMod.Update(exclusionForSourceURL: false);
                }

            }

            return success;
        }


        // Add a required DLC
        internal static void AddRequiredDLC(Mod catalogMod, Enums.DLC requiredDLC)
        {
            if (requiredDLC != default && !catalogMod.RequiredDLC.Contains(requiredDLC))
            {
                catalogMod.RequiredDLC.Add(requiredDLC);

                catalogMod.AddExclusionForRequiredDLC(requiredDLC);

                AddUpdatedModChangeNote(catalogMod, $"required DLC { requiredDLC } added");
            }
        }


        // Remove a required DLC
        internal static void RemoveRequiredDLC(Mod catalogMod, Enums.DLC requiredDLC)
        {
            if (catalogMod.RequiredDLC.Remove(requiredDLC))
            {
                catalogMod.ExclusionForRequiredDLC.Remove(requiredDLC);

                AddUpdatedModChangeNote(catalogMod, $"required DLC { requiredDLC } removed");
            }
        }


        // Add a required mod, including exclusion, required group and change notes
        internal static void AddRequiredMod(Mod catalogMod, ulong requiredID)
        {
            if (ActiveCatalog.Instance.IsValidID(requiredID, allowGroup: true) && !catalogMod.RequiredMods.Contains(requiredID))
            {
                catalogMod.RequiredMods.Add(requiredID);

                if (ActiveCatalog.Instance.GroupDictionary.ContainsKey(requiredID))
                {
                    // requiredID is a group
                    AddUpdatedModChangeNote(catalogMod, $"required group { requiredID } added");
                }
                else
                {
                    // requiredID is a mod
                    AddUpdatedModChangeNote(catalogMod, $"required mod { requiredID } added");

                    catalogMod.AddExclusionForRequiredMods(requiredID);

                    if (ActiveCatalog.Instance.IsGroupMember(requiredID))
                    {
                        // Also add the group that requiredID is a member of
                        AddRequiredMod(catalogMod, ActiveCatalog.Instance.GetGroup(requiredID).GroupID);
                    }
                }
            }

            // If the requiredID is not a known ID, it's probably an asset.
            else if (ActiveCatalog.Instance.IsValidID(requiredID, allowBuiltin: false, shouldExist: false) && !ActiveCatalog.Instance.RequiredAssets.Contains(requiredID))
            {
                UnknownRequiredAssets.Append($", { requiredID }");
                
                Logger.UpdaterLog($"Required item not found, probably an asset: { Toolkit.GetWorkshopURL(requiredID) } (for { catalogMod.ToString(cutOff: false) }).");
            }
        }


        // Remove a required mod from a mod, including exclusion, group and change notes
        internal static void RemoveRequiredMod(Mod catalogMod, ulong requiredID)
        {
            if (catalogMod.RequiredMods.Remove(requiredID))
            {
                if (ActiveCatalog.Instance.GroupDictionary.ContainsKey(requiredID))
                {
                    // requiredID is a group
                    AddUpdatedModChangeNote(catalogMod, $"required Group { requiredID } removed");
                }
                else
                {
                    // requiredID is a mod
                    AddUpdatedModChangeNote(catalogMod, $"required Mod { requiredID } removed");

                    // If an exclusion exists (it was added by FileImporter) remove it, otherwise (added by WebCrawler) add it to prevent the required mod from returning
                    if (catalogMod.ExclusionForRequiredMods.Contains(requiredID))
                    {
                        catalogMod.ExclusionForRequiredMods.Remove(requiredID);
                    }
                    else
                    {
                        catalogMod.ExclusionForRequiredMods.Add(requiredID);
                    }

                    Group group = ActiveCatalog.Instance.GetGroup(requiredID);

                    if (group != null)
                    {
                        // Check if none of the other group members is a required mod, so we can remove the group as required mod
                        bool canRemoveGroup = true;

                        foreach (ulong groupMember in group.GroupMembers)
                        {
                            canRemoveGroup = canRemoveGroup && !catalogMod.RequiredMods.Contains(groupMember);
                        }

                        if (canRemoveGroup)
                        {
                            RemoveRequiredMod(catalogMod, group.GroupID);
                        }
                    }
                }
            }
        }


        // Add a successor, including change notes
        internal static void AddSuccessor(Mod catalogMod, ulong successorID)
        {
            if (!catalogMod.Successors.Contains(successorID))
            {
                catalogMod.Successors.Add(successorID);

                AddUpdatedModChangeNote(catalogMod, $"successor { successorID } added");
            }
        }


        // Remove a successor, including change notes
        internal static void RemoveSuccessor(Mod catalogMod, ulong successorID)
        {
            if (catalogMod.Successors.Remove(successorID))
            {
                AddUpdatedModChangeNote(catalogMod, $"successor { successorID } removed");
            }
        }


        // Add an alternative, including change notes
        internal static void AddAlternative(Mod catalogMod, ulong alternativeID)
        {
            if (!catalogMod.Alternatives.Contains(alternativeID))
            {
                catalogMod.Alternatives.Add(alternativeID);

                AddUpdatedModChangeNote(catalogMod, $"alternative { alternativeID } added");
            }
        }


        // Remove an alternative, including change notes
        internal static void RemoveAlternative(Mod catalogMod, ulong alternativeID)
        {
            if (catalogMod.Alternatives.Remove(alternativeID))
            {
                AddUpdatedModChangeNote(catalogMod, $"alternative { alternativeID } removed");
            }
        }


        // Add a recommendation, including change notes
        internal static void AddRecommendation(Mod catalogMod, ulong recommendationID)
        {
            if (!catalogMod.Recommendations.Contains(recommendationID))
            {
                catalogMod.Recommendations.Add(recommendationID);

                AddUpdatedModChangeNote(catalogMod, $"successor { recommendationID } added");
            }
        }


        // Remove a successor, including change notes
        internal static void RemoveRecommendation(Mod catalogMod, ulong recommendationID)
        {
            if (catalogMod.Recommendations.Remove(recommendationID))
            {
                AddUpdatedModChangeNote(catalogMod, $"successor { recommendationID } removed");
            }
        }


        // Add a change note for an updated mod.
        internal static void AddUpdatedModChangeNote(Mod catalogMod, string extraChangeNote)
        {
            if (string.IsNullOrEmpty(extraChangeNote))
            {
                return;
            }

            // Add a separator if needed. The separator at the start of the final change note will be stripped before it is written.
            if (extraChangeNote[0] != ',')
            {
                extraChangeNote = ", " + extraChangeNote;
            }

            // Add the new change note to the dictionary
            if (changeNotesUpdatedMods.ContainsKey(catalogMod.SteamID))
            {
                changeNotesUpdatedMods[catalogMod.SteamID] += extraChangeNote;
            }
            else
            {
                changeNotesUpdatedMods.Add(catalogMod.SteamID, extraChangeNote);
            }
        }


        // Add a change note for a removed mod
        internal static void AddRemovedModChangeNote(string extraLine)
        {
            changeNotesRemovedMods.AppendLine(extraLine);
        }
        
        
        // Add a change note for an updated author.
        internal static void AddUpdatedAuthorChangeNote(Author catalogAuthor, string extraChangeNote)
        {
            // Add a separator if needed. The separator at the start of the final change note will be stripped before it is written.
            if (extraChangeNote[0] != ',')
            {
                extraChangeNote = ", " + extraChangeNote;
            }

            // Add the new change note to the dictionary
            if (catalogAuthor.ProfileID != 0)
            {
                if (changeNotesUpdatedAuthorsByID.ContainsKey(catalogAuthor.ProfileID))
                {
                    changeNotesUpdatedAuthorsByID[catalogAuthor.ProfileID] += extraChangeNote;
                }
                else
                {
                    changeNotesUpdatedAuthorsByID.Add(catalogAuthor.ProfileID, extraChangeNote);
                }
            }
            else
            {
                if (changeNotesUpdatedAuthorsByURL.ContainsKey(catalogAuthor.CustomURL))
                {
                    changeNotesUpdatedAuthorsByURL[catalogAuthor.CustomURL] += extraChangeNote;
                }
                else
                {
                    changeNotesUpdatedAuthorsByURL.Add(catalogAuthor.CustomURL, extraChangeNote);
                }
            }
        }


        // Add a change note for a catalog change
        internal static void AddCatalogChangeNote(string extraLine)
        {
            changeNotesCatalog.AppendLine(extraLine);
        }
    }
}
