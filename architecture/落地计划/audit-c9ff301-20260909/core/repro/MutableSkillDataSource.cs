using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
namespace Tests.Rules.Skill
{
    internal sealed class MutableSkillDataSource : IDataSource
    {
        private readonly Dictionary<string,string> _texts = new Dictionary<string,string>(StringComparer.Ordinal);
        public MutableSkillDataSource Add(string table, string text) { _texts[table] = text; return this; }
        public void Replace(string table, string text) { _texts[table] = text; }
        public IReadOnlyList<DataTableSource> ListTables()
        {
            var result = new List<DataTableSource>();
            foreach (var pair in _texts) { var table=pair.Key; result.Add(new DataTableSource(table, "memory://"+table, () => _texts[table])); }
            return result;
        }
    }
}
