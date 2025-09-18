using TOM = Microsoft.AnalysisServices.Tabular;
using System;
using System.Collections.Generic;
using System.Linq;
using TabularEditor.TOMWrapper;

namespace TabularEditor.TOMWrapper.Undo
{
    internal class UndoClearAction : IUndoAction
    {
        ITabularObjectCollection _collection;
        Tuple<TabularObject, Type, string>[] _objs;

        public UndoClearAction(ITabularObjectCollection collection, TabularObject[] content)
        {
            _collection = collection;
            _objs = content.Select(o => new Tuple<TabularObject, Type, string>(
                o, 
                o.MetadataObject.GetType(), 
                TOM.JsonSerializer.SerializeObject(o.MetadataObject, TabularObject.RenewMetadataOptions, o.Handler.CompatibilityLevel, o.Handler.Database.CompatibilityMode))).ToArray();
        }

        public string ActionName { get { return "clear"; } }

        public void Redo()
        {
            _collection.Clear();
        }

        public void Undo()
        {
            foreach (var obj in _objs) obj.Item1.Undelete(_collection, obj.Item2, obj.Item3);
        }

        public string GetSummary()
        {
            return string.Format("Cleared collection {{{0}}}", _collection.CollectionName);
        }

        public string GetCode()
        {
            return string.Empty;
        }
    }

}
