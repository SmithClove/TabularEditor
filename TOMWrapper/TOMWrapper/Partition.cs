using System;
using System.Linq;
using System.Collections.Generic;
using TabularEditor.PropertyGridUI;
using TOM = Microsoft.AnalysisServices.Tabular;
using System.Diagnostics;
using System.ComponentModel;
using TabularEditor.TOMWrapper.Undo;
using System.ComponentModel.Design;
using System.Drawing.Design;
using TabularEditor.TOMWrapper.PowerBI;
using TabularEditor.TOMWrapper.Utils;

namespace TabularEditor.TOMWrapper
{
    public partial class Partition: IExpressionObject, IDaxDependantObject
    {
        public bool NeedsValidation { get { return false; } private set { } }

        private DependsOnList _dependsOn = null;

        [Browsable(false)]
        public DependsOnList DependsOn
        {
            get
            {
                if (_dependsOn == null)
                    _dependsOn = new DependsOnList(this);
                return _dependsOn;
            }
        }

        protected override void Init()
        {
            if (MetadataObject.Source == null && !(Parent is CalculatedTable))
            {
                if (Model.DataSources.Count == 0) Model.AddDataSource();
                MetadataObject.Source = new TOM.QueryPartitionSource()
                {
                    DataSource = Model.DataSources.Any(ds => ds is ProviderDataSource) ?
                        Model.DataSources.First(ds => ds is ProviderDataSource).MetadataObject :
                        Model.DataSources.First().MetadataObject
                };
            }

            if (MetadataObject.DataCoverageDefinition != null) this.DataCoverageDefinition = DataCoverageDefinition.CreateFromMetadata(this, MetadataObject.DataCoverageDefinition);

            base.Init();
        }

        [Category("Basic"),Description("The query which is executed on the Data Source to populate this partition with data.")]
        [Editor(typeof(MultilineStringEditor), typeof(UITypeEditor)), IntelliSense("Gets or sets the query which is executed on the Data Source to populate the partition with data.")]
        public string Query { get { return Expression; } set { Expression = value; } }

        [Category("Basic"), Description("The expression which is used to populate this partition with data.")]
        [Editor(typeof(MultilineStringEditor), typeof(UITypeEditor))]
        public string Expression
        {
            get
            {
                switch(MetadataObject.SourceType)
                {
                    case TOM.PartitionSourceType.Calculated:
                        return (MetadataObject.Source as TOM.CalculatedPartitionSource)?.Expression;
                    case TOM.PartitionSourceType.Query:
                        return (MetadataObject.Source as TOM.QueryPartitionSource)?.Query;
                    case TOM.PartitionSourceType.M:
                        return (MetadataObject.Source as TOM.MPartitionSource)?.Expression;
                    default:
                        return null;
                }
            }
            set
            {
                var oldValue = Expression;
                if (oldValue == value) return;
                bool undoable = true;
                bool cancel = false;
                OnPropertyChanging("Expression", value, ref undoable, ref cancel);
                if (cancel) return;

                switch (MetadataObject.SourceType)
                {
                    case TOM.PartitionSourceType.Calculated:
                        (MetadataObject.Source as TOM.CalculatedPartitionSource).Expression = value; break;
                    case TOM.PartitionSourceType.Query:
                        (MetadataObject.Source as TOM.QueryPartitionSource).Query = value; break;
                    case TOM.PartitionSourceType.M:
                        (MetadataObject.Source as TOM.MPartitionSource).Expression = value; break;
                    default:
                        throw new NotSupportedException();
                }

                if (undoable) Handler.UndoManager.Add(new UndoPropertyChangedAction(this, "Expression", oldValue, value));
                OnPropertyChanged("Expression", oldValue, value);
            }
        }

        [Browsable(false)]
        public string DataCoverageDefinitionExpression
        {
            get
            {
                return DataCoverageDefinition?.Expression;
            }
            set
            {
                if (value == DataCoverageDefinitionExpression) return;
                if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(DataCoverageDefinitionExpression)) return;
                Handler.BeginUpdate("Data Coverage Definition Expression");
                if (DataCoverageDefinition == null) AddDataCoverageDefinition();
                if (DataCoverageDefinition != null) DataCoverageDefinition.Expression = value;
                Handler.EndUpdate();
            }
        }

        /// <summary>
        /// A reference to an optional DataCoverageDefinition that provides the hint regarding the data that is covered by the partition.
        /// </summary>
		[DisplayName("DataCoverageDefinition")]
        [Category("Options"), IntelliSense("A reference to an optional DataCoverageDefinition that provides the hint regarding the data that is covered by the partition.")]
        [PropertyAction(nameof(AddDataCoverageDefinition), nameof(RemoveDataCoverageDefinition)), Editor(typeof(DataCoverageDefinitionEditor), typeof(UITypeEditor))]
        public DataCoverageDefinition DataCoverageDefinition
        {
            get
            {
                if (MetadataObject.DataCoverageDefinition == null) return null;
                return Handler.WrapperLookup[MetadataObject.DataCoverageDefinition] as DataCoverageDefinition;
            }
            set
            {
                var oldValue = MetadataObject.DataCoverageDefinition != null ? DataCoverageDefinition : null;
                if (oldValue?.MetadataObject == value?.MetadataObject) return;
                bool undoable = true;
                bool cancel = false;
                OnPropertyChanging("DataCoverageDefinition", value, ref undoable, ref cancel);
                if (cancel) return;

                var newDataCoverageDefinition = value?.MetadataObject;
                if (newDataCoverageDefinition != null && newDataCoverageDefinition.IsRemoved)
                {
                    Handler.WrapperLookup.Remove(newDataCoverageDefinition);
                    newDataCoverageDefinition = newDataCoverageDefinition.Clone();
                    value.MetadataObject = newDataCoverageDefinition;
                    Handler.WrapperLookup.Add(newDataCoverageDefinition, value);
                }

                MetadataObject.DataCoverageDefinition = newDataCoverageDefinition;

                if (undoable) Handler.UndoManager.Add(new UndoPropertyChangedAction(this, "DataCoverageDefinition", oldValue, value));
                OnPropertyChanged("DataCoverageDefinition", oldValue, value);
            }
        }

        public DataCoverageDefinition AddDataCoverageDefinition()
        {
            Handler.BeginUpdate("Add DataCoverageDefinition");
            if (DataCoverageDefinition == null) DataCoverageDefinition = DataCoverageDefinition.CreateNew();
            Handler.EndUpdate();
            return DataCoverageDefinition;
        }
        private bool CanAddDataCoverageDefinition() => DataCoverageDefinition == null;
        public void RemoveDataCoverageDefinition()
        {
            Handler.BeginUpdate("Remove DataCoverageDefinition");
            DataCoverageDefinition = null;
            Handler.EndUpdate();
        }
        private bool CanRemoveDataCoverageDefinition() => DataCoverageDefinition != null;

        private DataCoverageDefinition DataCoverageDefinitionBackup;

        internal override void RemoveReferences()
        {
            DataCoverageDefinitionBackup = DataCoverageDefinition;
            base.RemoveReferences();
        }

        internal override void Reinit()
        {
            if (DataCoverageDefinitionBackup != null)
            {
                Handler.WrapperLookup.Remove(DataCoverageDefinitionBackup.MetadataObject);
                DataCoverageDefinitionBackup.MetadataObject = MetadataObject.DataCoverageDefinition;
                Handler.WrapperLookup.Add(DataCoverageDefinitionBackup.MetadataObject, DataCoverageDefinitionBackup);
            }
            base.Reinit();
        }

        [Browsable(false)]
        public ProviderDataSource ProviderDataSource => DataSource as ProviderDataSource;
        [Browsable(false)]
        public StructuredDataSource StructuredDataSource => DataSource as StructuredDataSource;

        [Category("Basic"), DisplayName("Data Source"), Description("The Data Source used by this partition."), TypeConverter(typeof(DataSourceConverter)), IntelliSense("The Data Source used by this partition.")]
        public DataSource DataSource
        {
            get
            {
                if (MetadataObject.Source is TOM.QueryPartitionSource)
                {
                    var ds = (MetadataObject.Source as TOM.QueryPartitionSource)?.DataSource;
                    return ds == null ? null : Handler.WrapperLookup[ds] as DataSource;
                }
                else if (MetadataObject.Source is TOM.EntityPartitionSource)
                {
                    var ds = (MetadataObject.Source as TOM.EntityPartitionSource)?.DataSource;
                    return ds == null ? null : Handler.WrapperLookup[ds] as DataSource;
                }
                else return null;
            }
            set
            {
                if (MetadataObject.Source is TOM.QueryPartitionSource qps)
                {
                    SetValue(DataSource, value, (v) => qps.DataSource = v?.MetadataObject);
                }
                else if (MetadataObject.Source is TOM.EntityPartitionSource eps)
                {
                    SetValue(DataSource, value, (v) => eps.DataSource = v?.MetadataObject);
                }
            }
        }

        private protected override bool IsBrowsable(string propertyName)
        {
            switch(propertyName)
            {
                case "DataSource":
                case "Query":
                    return SourceType == PartitionSourceType.Query;
                case Properties.EXPRESSION:
                    return SourceType == PartitionSourceType.Calculated || SourceType == PartitionSourceType.M;
                case Properties.MODE:
                case Properties.DATAVIEW:
                case Properties.DESCRIPTION:
                case Properties.NAME:
                case Properties.REFRESHEDTIME:
                case Properties.OBJECTTYPENAME:
                case Properties.STATE:
                case Properties.SOURCETYPE:
                case Properties.ANNOTATIONS:
                    return true;
                case nameof(DataCoverageDefinition):
                    return ShowDataCoverageDefinition();
                default:
                    return false;
            }
        }

        internal bool ShowDataCoverageDefinition() => Handler.CompatibilityLevel >= 1603 && (this.GetMode() == ModeType.DirectQuery || this.GetMode() == ModeType.Dual || DataCoverageDefinition != null);

        [Category("Metadata"),DisplayName("Last Processed")]
        public DateTime RefreshedTime
        {
            get { return MetadataObject.RefreshedTime; }
        }

        public override string Name
        {
            set
            {
                base.Name = value;
            }
            get
            {
                return base.Name;
            }
        }

        protected override bool AllowDelete(out string message)
        {
            if(Table.Partitions.Count == 1)
            {
                message = Messages.TableMustHaveAtLeastOnePartition;
                return false;
            }
            return base.AllowDelete(out message);
        }

        private protected override bool IsEditable(string propertyName)
        {
            switch(propertyName)
            {
                case Properties.NAME:
                case Properties.DESCRIPTION:
                case Properties.DATASOURCE:
                case Properties.QUERY:
                case Properties.EXPRESSION:
                case Properties.MODE:
                case Properties.DATAVIEW:
                case Properties.ANNOTATIONS:
                    return true;
                case nameof(DataCoverageDefinition):
                    return Handler.CompatibilityLevel >= 1603;
                default:
                    return false;
            }
        }
    }

    public partial class PartitionCollection: ITabularObjectContainer, ITabularTableObject
    {
        void ITabularObject.ReapplyReferences() => ReapplyReferences();
        void ITabularNamedObject.RemoveReferences() { }

        internal Type[] GetSupportedPartitionTypes()
        {
            if (Table.MetadataObject.RefreshPolicy is TOM.RefreshPolicy) return new[] { typeof(PolicyRangePartition) };
            var cl = Handler.CompatibilityLevel;
            if (cl >= 1400)
            {
                if (Handler.Model.DataSources.Any(ds => ds.Type == DataSourceType.Provider))
                    return cl >= 1561
                        ? new[] { typeof(Partition), typeof(MPartition), typeof(EntityPartition) }
                        : new[] { typeof(Partition), typeof(MPartition) };
                else
                    return cl >= 1561
                        ? new[] { typeof(MPartition), typeof(EntityPartition) }
                        : new[] { typeof(MPartition) };
            }
            else
                return new[] { typeof(Partition) };
        }

        bool ITabularNamedObject.CanEditName() { return false; }

        [IntelliSense("Converts all M partitions in this collection to regular partitions. The M query is left as-is and needs to be converted to SQL before the partition can be processed.")]
        public void ConvertToLegacy(ProviderDataSource providerSource = null)
        {
            Handler.BeginUpdate("Convert partitions");
            foreach(var oldPartition in this.OfType<MPartition>().ToList())
            {
                var newPartition = Partition.CreateNew(Table);
                newPartition.DataSource = providerSource == null ? oldPartition.DataSource : providerSource;
                newPartition.Expression = oldPartition.Expression;

                oldPartition.Delete();
                newPartition.Name = oldPartition.Name;
            }
            Handler.EndUpdate();
        }

        [IntelliSense("Converts all provider source partitions in this collection to M partitions. The provider query is left as-is and needs to be converted to an M query before the partition can be processed.")]
        public void ConvertToPowerQuery()
        {
            Handler.BeginUpdate("Convert partitions");
            foreach (var oldPartition in this.Where(p => p.GetType() == typeof(Partition)).ToList())
            {
                var newPartition = MPartition.CreateNew(Table);
                newPartition.DataSource = oldPartition.DataSource;
                newPartition.Expression = oldPartition.Query;

                oldPartition.Delete();
                newPartition.Name = oldPartition.Name;
            }
            Handler.EndUpdate();
        }

        /// <summary>
        /// This property points to the PartitionCollection itself. It is used only to display a clickable
        /// "Partitions" property in the Property Grid, which will open the PartitionCollectionEditor when
        /// clicked.
        /// </summary>
        [DisplayName("Partitions"),Description("The collection of Partition objects on this Table.")]
        [Category("Basic"), IntelliSense("The collection of Partition objects on this Table.")]
        [NoMultiselect(), Editor(typeof(PartitionCollectionEditor), typeof(UITypeEditor))]
        public PartitionCollection PropertyGridPartitions => this;

        bool ITabularObject.IsRemoved => false;

        int ITabularNamedObject.MetadataIndex => -1;

        Model ITabularObject.Model => Table.Model;

        [ReadOnly(true)]
        string ITabularNamedObject.Name
        {
            get
            {
                return "Partitions";
            }
            set
            {

            }
        }

        ObjectType ITabularObject.ObjectType => ObjectType.PartitionCollection;

        Table ITabularTableObject.Table => Table;

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add
            {
                throw new NotImplementedException();
            }

            remove
            {
                throw new NotImplementedException();
            }
        }

        bool ITabularNamedObject.CanDelete() => false;

        bool ITabularNamedObject.CanDelete(out string message)
        {
            message = Messages.CannotDeleteObject;
            return false;
        }

        void ITabularNamedObject.Delete()
        {
            throw new NotSupportedException();
        }

        IEnumerable<ITabularNamedObject> ITabularObjectContainer.GetChildren()
        {
            return this;
        }
    }

    internal static partial class Properties
    {
        public const string DATASOURCE = "DataSource";
        public const string QUERY = "Query";
    }
}
