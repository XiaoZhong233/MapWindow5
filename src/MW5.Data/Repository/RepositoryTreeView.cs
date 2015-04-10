﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MW5.Api.Static;
using MW5.Data.Enums;
using MW5.Data.Properties;
using MW5.Plugins.Enums;
using MW5.Plugins.Events;
using MW5.Plugins.Interfaces;
using MW5.Shared;
using MW5.UI.Controls;
using Syncfusion.Windows.Forms.Tools;

namespace MW5.Data.Repository
{
    public sealed class RepositoryTreeView: TreeViewBase, IRepositoryView
    {
        private IRepository _repository;
        private bool _initialized;

        public RepositoryTreeView()
        {
            BeforeExpand += TreeViewBeforeExpand;

            AfterSelect += RepositoryTreeView_AfterSelect;

            PrepareToolTip += RepositoryTreeView_PrepareToolTip;

            LoadOnDemand = true;

            ToolTipDuration = 3000;

            ItemDrag += RepositoryTreeView_ItemDrag;
        }

        public void InitRepository(IRepository repository)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("Attempt to initialize Repository tree view the second time.");
            }

            if (repository == null)
            {
                throw new ArgumentNullException("repository");
            }

            _repository = repository;

            PopulateTree();

            _repository.FolderAdded += RepositoryFolderAdded;
            _repository.FolderRemoved += RepositoryFolderRemoved;
            _repository.ConnectionAdded += RepositoryConnectionAdded;
            _repository.ConnectionRemoved += RepositoryConnectionRemoved;

            _initialized = true;
        }

        private void PopulateTree()
        {
            // databases
            var dbs = Items.AddItem(RepositoryItemType.Databases);
            var postGis = dbs.SubItems.AddItem(RepositoryItemType.PostGis);

            foreach (var cn in _repository.Connections.Where(cn => cn.DatabaseType == GeoDatabaseType.PostGis))
            {
                postGis.SubItems.AddDatabase(cn);
            }
            
            dbs.Expand();
            postGis.Expand();

            // file system
            var folders = Items.AddItem(RepositoryItemType.FileSystem);

            foreach (var path in _repository.Folders)
            {
                folders.SubItems.AddFolder(path, true);
            }

            folders.Expand();
        }

        private void RepositoryConnectionRemoved(object sender, ConnectionEventArgs e)
        {
            var item = GetDatabaseRoot(e.Connection.DatabaseType);
            var databaseItem = item.SubItems.OfType<IDatabaseItem>().FirstOrDefault(db => db.Connection == e.Connection);
            item.SubItems.Remove(databaseItem);
        }

        private void RepositoryConnectionAdded(object sender, ConnectionEventArgs e)
        {
            var item = GetDatabaseRoot(e.Connection.DatabaseType);
            item.SubItems.AddDatabase(e.Connection);
        }

        private void RepositoryFolderRemoved(object sender, FolderEventArgs e)
        {
            var item = GetSpecialItem(RepositoryItemType.FileSystem);
            var folder = item.SubItems.OfType<IFolderItem>().FirstOrDefault(f => f.GetPath().EqualsIgnoreCase(e.Path));
            if (folder != null)
            {
                item.SubItems.Remove(folder);
            }
        }

        private void RepositoryFolderAdded(object sender, FolderEventArgs e)
        {
            var item = GetSpecialItem(RepositoryItemType.FileSystem);
            item.SubItems.AddFolder(e.Path, true);
        }

        public event EventHandler<RepositoryEventArgs> ItemSelected;

        protected override IEnumerable<Bitmap> OnCreateImageList()
        {
            return new[]
            {
                Resources.img_hard_disk,
                Resources.img_folder_open,
                Resources.img_point,
                Resources.img_line,
                Resources.img_polygon,
                Resources.img_geometry,
                Resources.img_databases_16,
                Resources.img_database_16,
                Resources.img_postgis_16,
                Resources.img_raster
            };
        }

        public RepositoryItemCollection Items
        {
            get { return new RepositoryItemCollection(Nodes); }
        }

        public IRepositoryItem GetDatabaseRoot(GeoDatabaseType databaseType)
        {
            switch (databaseType)
            {
                case GeoDatabaseType.PostGis:
                    return GetSpecialItem(RepositoryItemType.PostGis);
                case GeoDatabaseType.SpatiaLite:
                case GeoDatabaseType.Oracle:
                case GeoDatabaseType.MsSql:
                default:
                    throw new ArgumentOutOfRangeException("databaseType");
            }
        }

        public IRepositoryItem GetSpecialItem(RepositoryItemType type)
        {
            var root = Items.FirstOrDefault(item => item.Type == type);
            
            if (root != null)
            {
                return root;
            }

            // let's seek the second level as well; currently we don't need a full blown recursion here
            foreach (var item in Items)
            {
                root = item.SubItems.FirstOrDefault(subItem => subItem.Type == type);
                if (root != null)
                {
                    return root;
                }
            }

            return null;
        }

        public IRepositoryItem SelectedItem
        {
            get
            {
                if (SelectedNode == null)
                {
                    return null;
                }

                return RepositoryItem.Get(SelectedNode);
            }
        }

        private void RepositoryTreeView_AfterSelect(object sender, EventArgs e)
        {
            FireItemSelected(SelectedNode);
        }

        private void TreeViewBeforeExpand(object sender, TreeViewAdvCancelableNodeEventArgs e)
        {
            var item = RepositoryItem.Get(e.Node);

            if (item.Type == RepositoryItemType.Folder || item.Type == RepositoryItemType.Database)
            {
                item.Expand();
            }
        }

        private void FireItemSelected(TreeNodeAdv node)
        {
            var handler = ItemSelected;
            if (handler != null)
            {
                var item = RepositoryItem.Get(node);
                handler(this, new RepositoryEventArgs(item));
            }
        }

        private void RepositoryTreeView_PrepareToolTip(object sender, ToolTipEventArgs e)
        {
            var item = RepositoryItem.Get(SelectedNode);

            switch (item.Type)
            {
                case RepositoryItemType.Image:
                case RepositoryItemType.Vector:
                    var fileItem = item as IFileItem;
                    if (fileItem != null)
                    {
                        string filename = fileItem.Filename;
                        PopulateToolTip(e.ToolTip, filename);
                    }
                    return;
                case RepositoryItemType.DatabaseLayer:
                    var layerItem = item as IDatabaseLayerItem;
                    PopulateToolTip(e.ToolTip, layerItem);
                    return;
            }

            e.Cancel = true;
        }

        private void PopulateToolTip(ToolTipInfo tooltip, IDatabaseLayerItem item)
        {
            tooltip.Header.Text = item.Name;

            tooltip.Body.Text = "Geometry type: " + item.GeometryType.EnumToString() + Environment.NewLine;
            tooltip.Body.Text += "Number of features: " +  item.NumFeatures + Environment.NewLine;
            tooltip.Body.Text += "Projection: " + item.Projection.ExportToProj4();
        }

        private void PopulateToolTip(ToolTipInfo tooltip, string filename)
        {
            tooltip.Header.Text = Path.GetFileName(filename);
            
            using (var ds = GeoSource.Open(filename))
            {
                tooltip.Body.Text = ds.ToolTipText;
            }
        }

        private void RepositoryTreeView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            var arr = e.Item as TreeNodeAdv[];
            if (arr == null || arr.Length == 0)
            {
                return;
            }

            var vectorItem = RepositoryItem.Get(arr[0]) as IFileItem;
            if (vectorItem != null)
            {
                DoDragDrop(vectorItem.Filename, DragDropEffects.Copy);
            }
        }
    }
}
