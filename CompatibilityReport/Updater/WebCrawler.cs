﻿using System;
using System.Diagnostics;
using System.IO;
using CompatibilityReport.CatalogData;
using CompatibilityReport.Util;

namespace CompatibilityReport.Updater
{
    /// <summary>WebCrawler gathers information from the Steam Workshop pages for all mods and updates the catalog with this.</summary>
    /// <remarks>This process takes roughly 15 minutes. The following information is gathered:<list type="bullet">
    /// <item>Mod: name, author, publish and update dates, source URL (GitHub links only), compatible game version (from tag), required DLCs, required mods,
    ///            incompatible stability, removed from or unlisted in the Steam Workshop status, no description status</item>
    /// <item>Author: name, Steam ID or Custom URL, last seen date (based on mod updates, not on comments), retired status (based on last seen date)</item></list></remarks>
    public static class WebCrawler
    {
        /// <summary>Starts the WebCrawler. Downloads Steam webpages for all mods and update the catalog with found information.</summary>
        public static void Start(Catalog catalog)
        {
            CatalogUpdater.SetReviewDate(DateTime.Now);

            if (GetBasicInfo(catalog))
            {
                GetDetails(catalog);
            }
        }


        /// <summary>Downloads 'mod listing' pages from the Steam Workshop to get mod names and IDs for all available mods.</summary>
        /// <returns>True if at least one mod was found, false otherwise.</returns>
        private static bool GetBasicInfo(Catalog catalog)
        {
            Logger.UpdaterLog("Updater started downloading Steam Workshop 'mod listing' pages. This should take less than 1 minute.");

            Stopwatch timer = Stopwatch.StartNew();
            string tempFileFullPath = Path.Combine(ModSettings.WorkPath, ModSettings.TempDownloadFileName);
            int totalMods = 0;
            int totalPages = 0;
            
            // Go through the different mod listings: mods and camera scripts, both regular and incompatible.
            foreach (string steamUrl in ModSettings.SteamModListingUrls)
            {
                Logger.UpdaterLog($"Starting downloads from { steamUrl }");
                
                int pageNumber = 0;

                // Download and read pages until we find no more mods, or we reach a maximum number of pages, to avoid missing the mark and continuing for eternity.
                while (pageNumber < ModSettings.SteamMaxModListingPages)
                {
                    pageNumber++;
                    string url = $"{ steamUrl }&p={ pageNumber }";

                    if (!Toolkit.Download(url, tempFileFullPath))
                    {
                        pageNumber--;

                        Logger.UpdaterLog($"Download process interrupted due to a permanent error while downloading { url }", Logger.Error);
                        break;
                    }

                    int modsFoundThisPage = ReadModListingPage(tempFileFullPath, catalog, incompatibleMods: steamUrl.Contains("incompatible"));

                    if (modsFoundThisPage == 0)
                    {
                        pageNumber--;

                        if (pageNumber == 0)
                        {
                            Logger.UpdaterLog("Found no mods on page 1.");
                        }

                        break;
                    }

                    totalMods += modsFoundThisPage;
                    Logger.UpdaterLog($"Found { modsFoundThisPage } mods on page { pageNumber }.");
                }

                totalPages += pageNumber;
            }

            Toolkit.DeleteFile(tempFileFullPath);

            // Note: about 75% of the total time is downloading, the other 25% is processing.
            timer.Stop();
            Logger.UpdaterLog($"Updater finished downloading { totalPages } Steam Workshop 'mod listing' pages in " +
                $"{ Toolkit.TimeString(timer.ElapsedMilliseconds) }. { totalMods } mods found.");

            return totalMods > 0;
        }


        /// <summary>Extracts Steam IDs and mod names for all mods from a downloaded mod listing page and adds/updates this in the catalog.</summary>
        /// <remarks>Sets the auto review date, (re)sets 'incompatible according to workshop' stability and removes unlisted and 'removed from workshop' statuses.</remarks>
        /// <returns>The number of mods found on this page.</returns>
        private static int ReadModListingPage(string tempFileFullPath, Catalog catalog, bool incompatibleMods)
        {
            int modsFoundThisPage = 0;
            string line;

            using (StreamReader reader = File.OpenText(tempFileFullPath))
            {
                while ((line = reader.ReadLine()) != null)
                {
                    // Search for the identifying string for the next mod; continue with next line if not found.
                    if (!line.Contains(ModSettings.SearchModStart))
                    {
                        continue;
                    }

                    ulong steamID = Toolkit.ConvertToUlong(Toolkit.MidString(line, ModSettings.SearchSteamIDLeft, ModSettings.SearchSteamIDRight));

                    if (steamID == 0) 
                    {
                        Logger.UpdaterLog($"Steam ID not recognized on HTML line: { line }", Logger.Error);
                        continue;
                    }

                    modsFoundThisPage++;

                    string modName = Toolkit.CleanHtml(Toolkit.MidString(line, ModSettings.SearchListingModNameLeft, ModSettings.SearchListingModNameRight));

                    if (string.IsNullOrEmpty(modName))
                    {
                        // An empty mod name might be an error, although there is a Steam Workshop mod without a name (ofcourse there is).
                        Logger.UpdaterLog($"Mod name not found for { steamID }. This could be an actual unnamed mod, or a Steam error.", Logger.Warning);
                    }

                    Mod catalogMod = catalog.GetMod(steamID) ?? CatalogUpdater.AddMod(catalog, steamID, modName, incompatibleMods);

                    CatalogUpdater.RemoveStatus(catalog, catalogMod, Enums.Status.RemovedFromWorkshop);
                    CatalogUpdater.RemoveStatus(catalog, catalogMod, Enums.Status.UnlistedInWorkshop);

                    if (incompatibleMods && catalogMod.Stability != Enums.Stability.IncompatibleAccordingToWorkshop)
                    {
                        CatalogUpdater.UpdateMod(catalog, catalogMod, stability: Enums.Stability.IncompatibleAccordingToWorkshop, stabilityNote: "");
                    }
                    else if (!incompatibleMods && catalogMod.Stability == Enums.Stability.IncompatibleAccordingToWorkshop)
                    {
                        CatalogUpdater.UpdateMod(catalog, catalogMod, stability: Enums.Stability.NotReviewed, stabilityNote: "");
                    }

                    CatalogUpdater.UpdateMod(catalog, catalogMod, modName, alwaysUpdateReviewDate: true);

                    // Author info can be found on the next line, but skip it here and get it later on the mod page.
                }
            }

            return modsFoundThisPage;
        }


        /// <summary>Downloads individual mod pages from the Steam Workshop to get detailed mod information for all mods in the catalog.</summary>
        /// <remarks>Known unlisted mods are included. Removed mods are checked, to catch reappearing mods.</remarks>
        private static void GetDetails(Catalog catalog)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string tempFileFullPath = Path.Combine(ModSettings.WorkPath, ModSettings.TempDownloadFileName);
            int numberOfMods = catalog.Mods.Count - ModSettings.BuiltinMods.Count;
            long estimate = 500 * numberOfMods;

            Logger.UpdaterLog($"Updater started downloading { numberOfMods } individual Steam Workshop mod pages. Estimated time: { Toolkit.TimeString(estimate) }.");

            int modsDownloaded = 0;
            int failedDownloads = 0;

            foreach (Mod catalogMod in catalog.Mods)
            {
                if (!catalog.IsValidID(catalogMod.SteamID, allowBuiltin: false))
                {
                    // Skip builtin mods.
                    continue;
                }

                if (!Toolkit.Download(Toolkit.GetWorkshopUrl(catalogMod.SteamID), tempFileFullPath))
                {
                    failedDownloads++;

                    if (failedDownloads <= ModSettings.SteamMaxFailedPages)
                    {
                        Logger.UpdaterLog("Permanent error while downloading Steam Workshop page for { catalogMod.ToString() }. Will continue with the next mod.", 
                            Logger.Error);

                        continue;
                    }
                    else
                    {
                        Logger.UpdaterLog("Permanent error while downloading Steam Workshop page for { catalogMod.ToString() }. Download process stopped.", 
                            Logger.Error);

                        break;
                    }
                }

                modsDownloaded++;

                if (modsDownloaded % 100 == 0)
                {
                    Logger.UpdaterLog($"{ modsDownloaded }/{ numberOfMods } mod pages downloaded.");
                }

                if (!ReadModPage(tempFileFullPath, catalog, catalogMod))
                {
                    // Redownload and try one more time, to work around cut-off downloads.
                    Toolkit.Download(Toolkit.GetWorkshopUrl(catalogMod.SteamID), tempFileFullPath);

                    ReadModPage(tempFileFullPath, catalog, catalogMod);
                }
            }

            Toolkit.DeleteFile(tempFileFullPath);

            // Note: about 90% of the total time is downloading, the other 10% is processing.
            timer.Stop();
            Logger.UpdaterLog($"Updater finished downloading { modsDownloaded } individual Steam Workshop mod pages in " + 
                $"{ Toolkit.TimeString(timer.ElapsedMilliseconds, alwaysShowSeconds: true) }.");

            Logger.Log($"Updater processed { modsDownloaded } Steam Workshop mod pages.");
        }


        /// <summary>Extracts detailed mod information from a downloaded mod page and updates the catalog.</summary>
        /// <remarks>Also sets the auto review date.</remarks>
        /// <returns>True if succesful, false if there was an error with the mod page.</returns>
        private static bool ReadModPage(string tempFileFullPath, Catalog catalog, Mod catalogMod)
        {
            bool steamIDmatched = false;
            string line;

            using (StreamReader reader = File.OpenText(tempFileFullPath))
            {
                while ((line = reader.ReadLine()) != null)
                {
                    // Only continue when we have found the correct Steam ID.
                    if (!steamIDmatched)
                    {
                        steamIDmatched = line.Contains($"{ ModSettings.SearchSteamID }{catalogMod.SteamID}");

                        if (steamIDmatched)
                        {
                            CatalogUpdater.RemoveStatus(catalog, catalogMod, Enums.Status.RemovedFromWorkshop);

                            if (!catalogMod.UpdatedThisSession)
                            {
                                CatalogUpdater.AddStatus(catalog, catalogMod, Enums.Status.UnlistedInWorkshop);
                                CatalogUpdater.UpdateMod(catalog, catalogMod, alwaysUpdateReviewDate: true);
                            }
                        }

                        else if (line.Contains(ModSettings.SearchItemNotFound))
                        {
                            if (catalogMod.UpdatedThisSession)
                            {
                                Logger.UpdaterLog($"We found this mod, but can't read the Steam page for { catalogMod.ToString() }. Mod info not updated.", Logger.Error);
                                return false;
                            }
                            else
                            {
                                CatalogUpdater.AddStatus(catalog, catalogMod, Enums.Status.RemovedFromWorkshop);
                                return true;
                            }
                        }

                        continue;
                    }

                    // Author Steam ID, Custom URL and author name.
                    // Todo 0.4 Add a check for author URL changes, to prevent creating a new author.
                    if (line.Contains(ModSettings.SearchAuthorLeft))
                    {
                        // Only get the author URL if the author ID was not found, to prevent updating the author URL to an empty string.
                        ulong authorID = Toolkit.ConvertToUlong(Toolkit.MidString(line, $"{ ModSettings.SearchAuthorLeft }profiles/", ModSettings.SearchAuthorMid));
                        string authorUrl = authorID != 0 ? null : Toolkit.MidString(line, $"{ ModSettings.SearchAuthorLeft }id/", ModSettings.SearchAuthorMid);
                        string authorName = Toolkit.CleanHtml(Toolkit.MidString(line, ModSettings.SearchAuthorMid, ModSettings.SearchAuthorRight));

                        if (string.IsNullOrEmpty(authorName))
                        {
                            Logger.UpdaterLog($"Author found without a name: { (authorID == 0 ? $"Custom URL { authorUrl }" : $"Steam ID { authorID }") }.", Logger.Error);
                        }
                        else if (authorName == authorID.ToString() && authorID != 0)
                        {
                            // An author name equal to the author ID might be an error, although some authors have their ID as name (ofcourse they do).
                            Logger.UpdaterLog($"Author found with Steam ID as name: { authorName }. Some authors do this, but it could also be a Steam error.", 
                                Logger.Warning);
                        }

                        Author catalogAuthor = catalog.GetAuthor(authorID, authorUrl) ?? CatalogUpdater.AddAuthor(catalog, authorID, authorUrl, authorName);
                        CatalogUpdater.UpdateAuthor(catalog, catalogAuthor, name: authorName);

                        CatalogUpdater.UpdateMod(catalog, catalogMod, authorID: catalogAuthor.SteamID, authorUrl: catalogAuthor.CustomUrl);
                    }

                    // Mod name.
                    else if (line.Contains(ModSettings.SearchModNameLeft))
                    {
                        string modName = Toolkit.CleanHtml(Toolkit.MidString(line, ModSettings.SearchModNameLeft, ModSettings.SearchModNameRight)); 

                        CatalogUpdater.UpdateMod(catalog, catalogMod, modName);
                    }

                    // Compatible game version tag
                    else if (line.Contains(ModSettings.SearchVersionTag))
                    {
                        // Convert the found tag to a game version and back to a formatted game version string, so we have a consistently formatted string.
                        string gameVersionString = Toolkit.MidString(line, ModSettings.SearchVersionTagLeft, ModSettings.SearchVersionTagRight);
                        Version gameVersion = Toolkit.ConvertToVersion(gameVersionString);
                        gameVersionString = Toolkit.ConvertGameVersionToString(gameVersion);

                        if (!catalogMod.ExclusionForGameVersion || gameVersion >= catalogMod.CompatibleGameVersion())
                        {
                            CatalogUpdater.UpdateMod(catalog, catalogMod, compatibleGameVersionString: gameVersionString);
                            catalogMod.UpdateExclusions(exclusionForGameVersion: false);
                        }
                    }

                    // Publish and update dates.
                    else if (line.Contains(ModSettings.SearchDates))
                    {
                        line = reader.ReadLine();
                        line = reader.ReadLine();
                        DateTime published = Toolkit.ConvertWorkshopDateTime(Toolkit.MidString(line, ModSettings.SearchDatesLeft, ModSettings.SearchDatesRight));

                        line = reader.ReadLine();
                        DateTime updated = Toolkit.ConvertWorkshopDateTime(Toolkit.MidString(line, ModSettings.SearchDatesLeft, ModSettings.SearchDatesRight));

                        CatalogUpdater.UpdateMod(catalog, catalogMod, published: published, updated: updated);
                    }

                    // Required DLC. This line can be found multiple times.
                    // Todo 0.4 Remove DLCs no longer required.
                    else if (line.Contains(ModSettings.SearchRequiredDLC))
                    {
                        line = reader.ReadLine();
                        Enums.Dlc dlc = Toolkit.ConvertToEnum<Enums.Dlc>(Toolkit.MidString(line, ModSettings.SearchRequiredDLCLeft, ModSettings.SearchRequiredDLCRight));

                        if (dlc != default && !catalogMod.ExclusionForRequiredDlc.Contains(dlc))
                        {
                            CatalogUpdater.AddRequiredDLC(catalog, catalogMod, dlc);
                        }
                    }

                    // Required mods and assets. The search string is a container with all required items on the next lines.
                    // Todo 0.4 Remove mods no longer required.
                    else if (line.Contains(ModSettings.SearchRequiredMod))
                    {
                        // Get all required items from the next lines, until we find no more. Max. 50 times to avoid an infinite loop.
                        for (var i = 1; i <= 50; i++)
                        {
                            line = reader.ReadLine();
                            ulong requiredID = Toolkit.ConvertToUlong(Toolkit.MidString(line, ModSettings.SearchRequiredModLeft, ModSettings.SearchRequiredModRight));

                            if (requiredID == 0)
                            {
                                break;
                            }

                            CatalogUpdater.AddRequiredMod(catalog, catalogMod, requiredID, updatedByImporter: false);

                            line = reader.ReadLine();
                            line = reader.ReadLine();
                            line = reader.ReadLine();
                        }
                    }

                    // Description for 'no description' status and for source URL.
                    else if (line.Contains(ModSettings.SearchDescription))
                    {
                        line = reader.ReadLine();

                        // The complete description is on one line. We can't search for the right part, because it might exist inside the description.
                        string description = Toolkit.MidString($"{ line }\n", ModSettings.SearchDescriptionLeft, "\n");
                        
                        // A 'no description' status is when the description is not at least a few characters longer than the mod name.
                        if ((description.Length <= catalogMod.Name.Length + ModSettings.SearchDescriptionRight.Length + 3) && !catalogMod.ExclusionForNoDescription)
                        {
                            CatalogUpdater.AddStatus(catalog, catalogMod, Enums.Status.NoDescription);
                        }
                        else if ((description.Length > catalogMod.Name.Length + 3) && !catalogMod.ExclusionForNoDescription)
                        {
                            CatalogUpdater.RemoveStatus(catalog, catalogMod, Enums.Status.NoDescription);
                        }

                        if (description.Contains(ModSettings.SearchSourceUrlLeft) && !catalogMod.ExclusionForSourceUrl)
                        {
                            CatalogUpdater.UpdateMod(catalog, catalogMod, sourceUrl: GetSourceUrl(description, catalogMod));
                        }

                        // Description is the last info we need from the page, so break out of the while loop.
                        break;
                    }

                    // Todo 0.4 Can we get the NoCommentSection status automatically?
                }
            }

            if (!steamIDmatched && !catalogMod.Statuses.Contains(Enums.Status.RemovedFromWorkshop))
            {
                // We didn't find a Steam ID on the page, but no error page either. Must be a download issue or another Steam error.
                Logger.UpdaterLog($"Can't find the Steam ID on downloaded page for { catalogMod.ToString() }. Mod info not updated.", Logger.Error);
                return false;
            }

            return true;
        }


        /// <summary>Gets the source URL.</summary>
        /// <remarks>If more than one is found, pick the most likely, which is far from perfect and might need a CSV update to set it right.</remarks>
        /// <returns>The source URL string.</returns>
        private static string GetSourceUrl(string modDescription, Mod catalogMod)
        {
            string sourceUrl = $"https://github.com/{ Toolkit.MidString(modDescription, ModSettings.SearchSourceUrlLeft, ModSettings.SearchSourceUrlRight) }";
            string currentLower = sourceUrl.ToLower();

            if (sourceUrl == "https://github.com/")
            {
                return null;
            }

            // Some commonly listed source URLs to always ignore: Pardeike's Harmony and Sschoener's detour
            const string pardeike  = "https://github.com/pardeike";
            const string sschoener = "https://github.com/sschoener/cities-skylines-detour";

            string discardedUrls = "";
            int tries = 0;

            // Keep comparing source URLs until we find no more. Max. 50 times to avoid infinite loops.
            while (modDescription.IndexOf(ModSettings.SearchSourceUrlLeft) != modDescription.LastIndexOf(ModSettings.SearchSourceUrlLeft) && tries < 50)
            {
                tries++;

                int index = modDescription.IndexOf(ModSettings.SearchSourceUrlLeft) + 1;
                modDescription = modDescription.Substring(index);

                string nextSourceUrl = $"https://github.com/{ Toolkit.MidString(modDescription, ModSettings.SearchSourceUrlLeft, ModSettings.SearchSourceUrlRight) }";
                string nextLower = nextSourceUrl.ToLower();

                // Decide on which source URL to use.
                if (nextLower == "https://github.com/" || nextLower == currentLower || nextLower.Contains(pardeike) || nextLower.Contains(sschoener))
                {
                    // Silently discard the new source URL.
                }
                else if (currentLower.Contains(pardeike) || currentLower.Contains(sschoener))
                {
                    // Silently discard the old source URL.
                    sourceUrl = nextSourceUrl;
                }
                else if (currentLower.Contains("issue") || currentLower.Contains("wiki") || currentLower.Contains("documentation") 
                    || currentLower.Contains("readme") || currentLower.Contains("guide") || currentLower.Contains("translation"))
                {
                    discardedUrls += $"\n                      Discarded: { sourceUrl }";
                    sourceUrl = nextSourceUrl;
                }
                else
                {
                    discardedUrls += $"\n                      Discarded: { nextSourceUrl }";
                }

                currentLower = sourceUrl.ToLower();
            }

            // Discard the selected source URL if it is Pardeike or Sschoener. This happens when that is the only GitHub link in the description.
            if (currentLower.Contains(pardeike) || currentLower.Contains(sschoener))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(discardedUrls) && sourceUrl != catalogMod.SourceUrl)
            {
                Logger.UpdaterLog($"Found multiple source URLs for { catalogMod.ToString() }\n                      Selected:  { sourceUrl }{ discardedUrls }");
            }

            return sourceUrl;
        }
    }
}
