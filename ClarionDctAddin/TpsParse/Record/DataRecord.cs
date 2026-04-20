using System.Collections.Generic;
using System.IO;
using TpsParse.Util;

namespace TpsParse.Tps.Record
{
    public class DataRecord : TpsRecord
    {
        public DataRecord(byte[] data, byte[] header)
            : base(data, header, true)
        {
            RecordNumber = BitUtil.ToInt32(header, 5, false);
        }

        public int RecordNumber { get; private set; }
        public List<object> Values { get; private set; }

        /// <summary>
        ///     To parse the values in a field, you must pass the table definition.
        /// </summary>
        /// <param name="record">Table definition of the data field.</param>
        public void ParseValues(TableDefinitionRecord record)
        {
            // Seek to each field's declared Offset before reading. The
            // original upstream sequential read drifts when the DCT uses
            // OVER()-style aliased fields (common in Clarion — multiple
            // named fields share the same bytes as a parent group), so
            // PASSWORD and such come out as mid-string garbage. Honoring
            // Offset fixes both the OVER() case and any gaps between
            // fields, at the cost of one extra seek per field.
            var values = new List<object>(record.Fields.Count);
            using (var stream = new MemoryStream(Data))
            {
                foreach (var field in record.Fields)
                {
                    if (field.Offset >= 0 && field.Offset <= Data.Length)
                        stream.Position = field.Offset;
                    values.Add(field.IsArray() ? field.GetArrayValue(stream) : field.GetValue(stream));
                }
            }
            Values = values;
        }

        public override T Accept<T>(ITpsFileVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }
}