﻿using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using NuGet.VisualStudio;

namespace WebCompilerVsix.Commands
{
    internal sealed class CompileOnBuild
    {
        private readonly Package _package;
        private bool _isInstalled;

        private CompileOnBuild(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            _package = package;

            OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(GuidList.guidCompilerCmdSet, PackageCommands.CompileOnBuild);
                var menuItem = new OleMenuCommand(EnableCompileOnBuild, menuCommandID);
                menuItem.BeforeQueryStatus += BeforeQueryStatus;
                commandService.AddCommand(menuItem);
            }
        }

        private void BeforeQueryStatus(object sender, EventArgs e)
        {
            var button = (OleMenuCommand)sender;
            var item = ProjectHelpers.GetSelectedItems().FirstOrDefault();

            if (item == null || item.ContainingProject == null || item.Properties == null)
            {
                button.Visible = false;
                return;
            }

            var sourceFile = item.Properties.Item("FullPath").Value.ToString();
            bool isConfigFile = Path.GetFileName(sourceFile).Equals(FileHelpers.FILENAME, StringComparison.OrdinalIgnoreCase);

            if (!isConfigFile)
            {
                button.Visible = false;
                return;
            }

            // Some projects don't have a .csproj file and will therefore not be able to execute the build task.
            if (item.ContainingProject.Kind.Equals("{E24C65DC-7377-472B-9ABA-BC803B73C61A}", StringComparison.OrdinalIgnoreCase) || // Website Project
                item.ContainingProject.Kind.Equals("{8BB2217D-0F2D-49D1-97BC-3654ED321F3B}", StringComparison.OrdinalIgnoreCase))   // ASP.NET 5
            {
                button.Enabled = false;
                return;
            }

            button.Visible = isConfigFile;

            if (button.Visible)
            {
                _isInstalled = IsPackageInstalled(item.ContainingProject);
                button.Checked = _isInstalled;
            }
        }

        public static CompileOnBuild Instance
        {
            get;
            private set;
        }

        private IServiceProvider ServiceProvider
        {
            get
            {
                return _package;
            }
        }

        public static void Initialize(Package package)
        {
            Instance = new CompileOnBuild(package);
        }

        private void EnableCompileOnBuild(object sender, EventArgs e)
        {
            var item = ProjectHelpers.GetSelectedItems().FirstOrDefault();

            if (item == null)
                return;

            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));

            if (!_isInstalled)
            {
                var question = MessageBox.Show("A NuGet package will be installed to augment the MSBuild process, but no files will be added to the project.\rThis may require an internet connection.\r\rDo you want to continue?", "Web Compiler", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (question == DialogResult.No)
                    return;

                Version version = new Version(WebCompilerPackage.Version);
                if (version == new Version(1, 0, 4))
                    version = (Version)null;

                System.Threading.ThreadPool.QueueUserWorkItem((o) =>
                {
                    try
                    {
                        WebCompilerPackage._dte.StatusBar.Text = @"Installing BuildWebCompiler NuGet package, this may take a minute...";
                        WebCompilerPackage._dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationSync);

                        var installer = componentModel.GetService<IVsPackageInstaller>();
                        installer.InstallPackage(null, item.ContainingProject, "BuildWebCompiler", version, false);

                        WebCompilerPackage._dte.StatusBar.Text = @"Finished installing the BuildWebCompiler NuGet package";
                    }
                    catch
                    {
                        WebCompilerPackage._dte.StatusBar.Text = @"Unable to install the BuildWebCompiler NuGet package";
                    }
                    finally
                    {
                        WebCompilerPackage._dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationSync);
                    }
                });
            }
            else
            {
                System.Threading.ThreadPool.QueueUserWorkItem((o) =>
                {
                    try
                    {
                        WebCompilerPackage._dte.StatusBar.Text = @"Uninstalling BuildWebCompiler NuGet package, this may take a minute...";
                        WebCompilerPackage._dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationSync);
                        var uninstaller = componentModel.GetService<IVsPackageUninstaller>();
                        uninstaller.UninstallPackage(item.ContainingProject, "BuildWebCompiler", false);

                        WebCompilerPackage._dte.StatusBar.Text = @"Finished uninstalling the BuildWebCompiler NuGet package";
                    }
                    catch
                    {
                        WebCompilerPackage._dte.StatusBar.Text = @"Unable to ininstall the BuildWebCompiler NuGet package";
                    }
                    finally
                    {
                        WebCompilerPackage._dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationSync);
                    }
                });
            }
        }

        private bool IsPackageInstalled(Project project)
        {
            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            IVsPackageInstallerServices installerServices = componentModel.GetService<IVsPackageInstallerServices>();

            return installerServices.IsPackageInstalled(project, "BuildWebCompiler");
        }
    }
}
