﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Dogfood.Exports;
using Microsoft.VisualStudio.ExtensionManager;
using DTE = EnvDTE.DTE;
using Task = System.Threading.Tasks.Task;

namespace Dogfood.Services
{
    public class DogfoodService : IDogfoodService
    {
        IServiceProvider serviceProvider;
        IProjectUtilities projectUtilities;

        public DogfoodService(IServiceProvider serviceProvider, IProjectUtilities projectUtilities)
        {
            this.serviceProvider = serviceProvider;
            this.projectUtilities = projectUtilities;
        }

        public async Task<bool> Reinstall(string vsixFile, IProgress<string> progress)
        {
            var em = ExtensionManager();
            var installableExtension = em.CreateInstallableExtension(vsixFile);
            var identifier = installableExtension.Header.Identifier;

            var uninstalled = await Uninstall(identifier, progress);
            if (!uninstalled)
            {
                return false;
            }

            var header = installableExtension.Header;
            if (header.AllUsers)
            {
                SetValue(header, nameof(header.AllUsers), false);
                progress.Report($"Changed extension to AllUsers={header.AllUsers}");
            }

            SetValue(header, nameof(header.LocalizedName), header.LocalizedName + " [Dogfood]");

            progress.Report("Installing " + header.Name + " from " + vsixFile);
            await Task.Run(() => em.Install(installableExtension, false));

            var installedExt = em.GetInstalledExtension(identifier);
            ReportContents(progress, installedExt);

            em.Enable(installedExt);
            progress.Report("Please restart Visual Studio");

            return true;
        }

        async Task<bool> Uninstall(string identifier, IProgress<string> progress)
        {
            var em = ExtensionManager();

            if (!em.TryGetInstalledExtension(identifier, out IInstalledExtension previousExt))
            {
                // nothing to uninstall
                return true;
            }

            if (previousExt.Header.AllUsers)
            {
                progress.Report("Admin rights are requred to uninstall AllUsers=true extension.");

                var dte = Dte();
                if (StartUninstall(dte, identifier, progress))
                {
                    progress.Report("Please close Visual Studio, uninstall using VSIXInstaller and try again.");
                }

                return false;
            }

            progress.Report("Uninstalling " + previousExt.Header.Name);
            await Task.Run(() => em.Uninstall(previousExt));
            return true;
        }

        static bool StartUninstall(DTE dte, string identifier, IProgress<string> progress)
        {
            var dir = Path.GetDirectoryName(dte.FileName);
            var application = Path.Combine(dir, "VSIXInstaller.exe");
            if (!File.Exists(application))
            {
                progress.Report($"Couldn't find application at: {application}");
                return false;
            }

            var startInfo = new ProcessStartInfo(application, $"/uninstall:{identifier}");
            Process.Start(startInfo);
            return true;
        }

        static void ReportContents(IProgress<string> progress, IInstalledExtension installedExt)
        {
            progress.Report(installedExt.InstallPath);
            foreach (var content in installedExt.Content)
            {
                progress.Report($"  {content.RelativePath} ({content.ContentTypeName})");
            }
        }

        static void SetValue(object target, string name, object value)
        {
            target.GetType().GetProperty(name).SetValue(target, value);
        }

        IVsExtensionManager ExtensionManager() => (IVsExtensionManager)serviceProvider.GetService(typeof(SVsExtensionManager));

        DTE Dte() => (DTE)serviceProvider.GetService(typeof(DTE));
    }
}
