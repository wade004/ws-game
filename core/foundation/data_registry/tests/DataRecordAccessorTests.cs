using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// <see cref="DataRecord"/> 类型化访问器的逐访问器用例（T-M3，2026-10-01 测试覆盖第四批）：
    /// 每个 <c>GetXxx</c> 在"字段缺失 / 为 null / 类型不符"三种情形下抛 <see cref="DataFieldException"/>，
    /// 且异常携带表名、记录主键、字段名并在消息里给出"期望…实际…"；每个 <c>TryGetXxx</c> 同样情形返回
    /// false 且输出值为该类型的零值、不抛异常；正常取值与 <c>Has</c> 语义。
    /// </summary>
    public sealed class DataRecordAccessorTests
    {
        private const string TableName = "test.table";
        private const string RecordKey = "test.rec";

        private static DataRecord Record(string json)
        {
            var table = new TableSchema(TableName, "id", 1, new List<FieldSchema>());
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(table, RecordKey, new Id(RecordKey), raw);
        }

        private static DataFieldException AssertFieldError(Action act, string field, params string[] messageParts)
        {
            var ex = Assert.Throws<DataFieldException>(act);
            Assert.Equal(TableName, ex.Table);
            Assert.Equal(RecordKey, ex.RecordKey);
            Assert.Equal(field, ex.Field);
            Assert.Contains(TableName, ex.Message);
            Assert.Contains(RecordKey, ex.Message);
            Assert.Contains(field, ex.Message);
            foreach (var part in messageParts)
            {
                Assert.Contains(part, ex.Message);
            }

            return ex;
        }

        // -----------------------------------------------------------------
        // 构造 / Has
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_NullArguments_Throw()
        {
            var table = new TableSchema(TableName, "id", 1, new List<FieldSchema>());
            var raw = (JsonObject)JsonReader.Parse("{}");

            Assert.Throws<ArgumentNullException>(() => new DataRecord(null!, RecordKey, null, raw));
            Assert.Throws<ArgumentNullException>(() => new DataRecord(table, null!, null, raw));
            Assert.Throws<ArgumentNullException>(() => new DataRecord(table, RecordKey, null, null!));
        }

        [Fact]
        public void Has_TrueOnlyForPresentNonNullFields()
        {
            var r = Record("{\"a\": 0, \"b\": null, \"c\": \"\", \"d\": false}");

            Assert.True(r.Has("a"));
            Assert.False(r.Has("b"));
            Assert.True(r.Has("c"));
            Assert.True(r.Has("d"));
            Assert.False(r.Has("missing"));
        }

        // -----------------------------------------------------------------
        // 缺失 / null：全部 Get 抛 "字段缺失或为 null"
        // -----------------------------------------------------------------

        public static IEnumerable<object[]> AllGetters()
        {
            yield return new object[] { "GetString", (Func<DataRecord, object>)(r => r.GetString("f")) };
            yield return new object[] { "GetInt", (Func<DataRecord, object>)(r => r.GetInt("f")) };
            yield return new object[] { "GetNumber", (Func<DataRecord, object>)(r => r.GetNumber("f")) };
            yield return new object[] { "GetBool", (Func<DataRecord, object>)(r => r.GetBool("f")) };
            yield return new object[] { "GetId", (Func<DataRecord, object>)(r => r.GetId("f")) };
            yield return new object[] { "GetIdList", (Func<DataRecord, object>)(r => r.GetIdList("f")) };
            yield return new object[] { "GetVec2", (Func<DataRecord, object>)(r => r.GetVec2("f")) };
            yield return new object[] { "GetObject", (Func<DataRecord, object>)(r => r.GetObject("f")) };
            yield return new object[] { "GetArray", (Func<DataRecord, object>)(r => r.GetArray("f")) };
        }

        [Theory]
        [MemberData(nameof(AllGetters))]
        public void Get_FieldMissing_ThrowsDataFieldExceptionNamingTableRecordAndField(
            string accessor, Func<DataRecord, object> get)
        {
            var r = Record("{}");

            AssertFieldError(() => get(r), "f", "字段缺失或为 null");
            Assert.False(string.IsNullOrEmpty(accessor));
        }

        [Theory]
        [MemberData(nameof(AllGetters))]
        public void Get_FieldExplicitNull_IsTreatedAsMissing(string accessor, Func<DataRecord, object> get)
        {
            var r = Record("{\"f\": null}");

            AssertFieldError(() => get(r), "f", "字段缺失或为 null");
            Assert.False(string.IsNullOrEmpty(accessor));
        }

        // -----------------------------------------------------------------
        // 类型不符：Get 抛 "期望 X，实际 Y"
        // -----------------------------------------------------------------

        [Fact]
        public void GetString_WrongType_ReportsExpectedAndActualKind()
        {
            AssertFieldError(() => Record("{\"f\": 1}").GetString("f"), "f", "期望 String", "实际 Number");
            AssertFieldError(() => Record("{\"f\": true}").GetString("f"), "f", "实际 Bool");
            AssertFieldError(() => Record("{\"f\": []}").GetString("f"), "f", "实际 Array");
            AssertFieldError(() => Record("{\"f\": {}}").GetString("f"), "f", "实际 Object");
        }

        [Fact]
        public void GetInt_NonIntegerOrWrongType_ReportsExpectedInt()
        {
            AssertFieldError(() => Record("{\"f\": 1.5}").GetInt("f"), "f", "期望 Int");
            AssertFieldError(() => Record("{\"f\": \"3\"}").GetInt("f"), "f", "期望 Int", "实际 String");
            AssertFieldError(() => Record("{\"f\": 1e30}").GetInt("f"), "f", "期望 Int");
        }

        [Fact]
        public void GetNumber_WrongType_ReportsExpectedNumber()
        {
            AssertFieldError(() => Record("{\"f\": \"1\"}").GetNumber("f"), "f", "期望 Number", "实际 String");
        }

        [Fact]
        public void GetBool_WrongType_ReportsExpectedBool()
        {
            AssertFieldError(() => Record("{\"f\": 0}").GetBool("f"), "f", "期望 Bool", "实际 Number");
            AssertFieldError(() => Record("{\"f\": \"true\"}").GetBool("f"), "f", "期望 Bool", "实际 String");
        }

        [Fact]
        public void GetId_NotAString_OrMalformedId_Throws()
        {
            AssertFieldError(() => Record("{\"f\": 5}").GetId("f"), "f", "期望合法 Id 字符串", "实际 Number");
            AssertFieldError(() => Record("{\"f\": \"Not An Id\"}").GetId("f"), "f", "期望合法 Id 字符串");
            AssertFieldError(() => Record("{\"f\": \"\"}").GetId("f"), "f", "期望合法 Id 字符串");
        }

        [Fact]
        public void GetIdList_NotAnArray_ReportsExpectedIdArray()
        {
            AssertFieldError(() => Record("{\"f\": \"item.a\"}").GetIdList("f"), "f", "期望 Id 数组", "实际 String");
        }

        [Fact]
        public void GetIdList_BadElement_ReportsElementIndex()
        {
            AssertFieldError(() => Record("{\"f\": [\"item.a\", 7, \"item.c\"]}").GetIdList("f"), "f", "第 1 个元素");
            AssertFieldError(() => Record("{\"f\": [\"item.a\", \"item.b\", \"BAD ID\"]}").GetIdList("f"), "f", "第 2 个元素");
        }

        [Fact]
        public void GetVec2_MissingComponentOrWrongComponentType_Throws()
        {
            AssertFieldError(() => Record("{\"f\": {\"x\": 1}}").GetVec2("f"), "f", "Vec2");
            AssertFieldError(() => Record("{\"f\": {\"y\": 1}}").GetVec2("f"), "f", "Vec2");
            AssertFieldError(() => Record("{\"f\": {\"x\": \"1\", \"y\": 2}}").GetVec2("f"), "f", "Vec2");
            AssertFieldError(() => Record("{\"f\": {\"x\": 1, \"y\": null}}").GetVec2("f"), "f", "Vec2");
            AssertFieldError(() => Record("{\"f\": [1, 2]}").GetVec2("f"), "f", "Vec2");
            AssertFieldError(() => Record("{\"f\": 3}").GetVec2("f"), "f", "Vec2");
        }

        [Fact]
        public void GetObject_And_GetArray_WrongType_ReportExpected()
        {
            AssertFieldError(() => Record("{\"f\": []}").GetObject("f"), "f", "期望 Object", "实际 Array");
            AssertFieldError(() => Record("{\"f\": {}}").GetArray("f"), "f", "期望 Array", "实际 Object");
            AssertFieldError(() => Record("{\"f\": \"x\"}").GetObject("f"), "f", "实际 String");
            AssertFieldError(() => Record("{\"f\": 1}").GetArray("f"), "f", "实际 Number");
        }

        // -----------------------------------------------------------------
        // 正常取值
        // -----------------------------------------------------------------

        [Fact]
        public void Get_WellTypedValues_ReturnThem()
        {
            var r = Record(
                "{\"s\": \"hello\", \"i\": -7, \"n\": 2.5, \"b\": true, \"id\": \"item.sword\"," +
                " \"ids\": [\"item.a\", \"item.b\"], \"v\": {\"x\": 1.5, \"y\": -2}, \"o\": {\"k\": 1}, \"a\": [1, 2, 3]}");

            Assert.Equal("hello", r.GetString("s"));
            Assert.Equal(-7L, r.GetInt("i"));
            Assert.Equal(2.5, r.GetNumber("n"));
            Assert.True(r.GetBool("b"));
            Assert.Equal(new Id("item.sword"), r.GetId("id"));
            Assert.Equal(new[] { new Id("item.a"), new Id("item.b") }, r.GetIdList("ids"));
            Assert.Equal(new Vec2(1.5, -2), r.GetVec2("v"));
            Assert.Single(r.GetObject("o"));
            Assert.Equal(3, r.GetArray("a").Count);
        }

        [Fact]
        public void GetInt_LiteralWithDecimalPoint_IsRejected_ButGetNumberAcceptsBothForms()
        {
            // JsonNumber.TryGetInt64 要求原文不带小数点/指数（"3.0" 不是整数字面量）。
            var r = Record("{\"i\": 3.0, \"n\": 4}");

            AssertFieldError(() => r.GetInt("i"), "i", "期望 Int");
            Assert.False(r.TryGetInt("i", out _));
            Assert.Equal(3.0, r.GetNumber("i"));
            Assert.Equal(4.0, r.GetNumber("n"));
            Assert.Equal(4L, r.GetInt("n"));
        }

        [Fact]
        public void GetIdList_EmptyArray_ReturnsEmptyList()
        {
            Assert.Empty(Record("{\"f\": []}").GetIdList("f"));
        }

        // -----------------------------------------------------------------
        // Try*：缺失 / null / 类型不符 → false + 零值，永不抛
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"f\": null}")]
        [InlineData("{\"f\": [1]}")]
        public void TryGetString_Int_Number_Bool_Id_Vec2_Object_NotMatching_ReturnFalseWithZeroValue(string json)
        {
            var r = Record(json);
            const string f = "f";

            Assert.False(r.TryGetString(f, out var s));
            Assert.Equal(string.Empty, s);
            Assert.False(r.TryGetInt(f, out var i));
            Assert.Equal(0L, i);
            Assert.False(r.TryGetNumber(f, out var n));
            Assert.Equal(0.0, n);
            Assert.False(r.TryGetBool(f, out var b));
            Assert.False(b);
            Assert.False(r.TryGetId(f, out var id));
            Assert.Equal(default(Id), id);
            Assert.False(r.TryGetVec2(f, out var v));
            Assert.Equal(Vec2.Zero, v);
            Assert.False(r.TryGetObject(f, out var o));
            Assert.Null(o);
        }

        [Fact]
        public void TryGetArray_And_TryGetIdList_NotMatching_ReturnFalse()
        {
            foreach (var json in new[] { "{}", "{\"f\": null}", "{\"f\": {}}", "{\"f\": \"x\"}", "{\"f\": 1}" })
            {
                var r = Record(json);
                Assert.False(r.TryGetArray("f", out var a));
                Assert.Null(a);
                Assert.False(r.TryGetIdList("f", out var ids));
                Assert.Empty(ids);
            }
        }

        [Fact]
        public void TryGetInt_NonIntegral_ReturnsFalse_ButTryGetNumberSucceeds()
        {
            var r = Record("{\"f\": 1.5}");

            Assert.False(r.TryGetInt("f", out var i));
            Assert.Equal(0L, i);
            Assert.True(r.TryGetNumber("f", out var n));
            Assert.Equal(1.5, n);
        }

        [Fact]
        public void TryGetId_MalformedString_ReturnsFalse()
        {
            var r = Record("{\"f\": \"Not An Id\"}");

            Assert.False(r.TryGetId("f", out var id));
            Assert.Equal(default(Id), id);
            Assert.True(r.TryGetString("f", out var s));
            Assert.Equal("Not An Id", s);
        }

        [Fact]
        public void TryGetIdList_BadElement_ReturnsFalseWithEmptyList_NotPartialList()
        {
            var r = Record("{\"f\": [\"item.a\", 7]}");

            Assert.False(r.TryGetIdList("f", out var ids));
            Assert.Empty(ids);
        }

        [Fact]
        public void TryGetVec2_IncompleteOrNonNumeric_ReturnsFalseWithZero()
        {
            foreach (var json in new[]
            {
                "{\"f\": {\"x\": 1}}",
                "{\"f\": {\"x\": \"1\", \"y\": 2}}",
                "{\"f\": {\"x\": 1, \"y\": true}}",
            })
            {
                Assert.False(Record(json).TryGetVec2("f", out var v));
                Assert.Equal(Vec2.Zero, v);
            }
        }

        [Fact]
        public void Try_WellTypedValues_ReturnTrueAndValue()
        {
            var r = Record(
                "{\"s\": \"x\", \"i\": 5, \"n\": 0.25, \"b\": false, \"id\": \"unit.a\"," +
                " \"ids\": [\"unit.a\"], \"v\": {\"x\": 3, \"y\": 4}, \"o\": {}, \"a\": []}");

            Assert.True(r.TryGetString("s", out var s));
            Assert.Equal("x", s);
            Assert.True(r.TryGetInt("i", out var i));
            Assert.Equal(5L, i);
            Assert.True(r.TryGetNumber("n", out var n));
            Assert.Equal(0.25, n);
            Assert.True(r.TryGetBool("b", out var b));
            Assert.False(b);
            Assert.True(r.TryGetId("id", out var id));
            Assert.Equal(new Id("unit.a"), id);
            Assert.True(r.TryGetIdList("ids", out var ids));
            Assert.Equal(new[] { new Id("unit.a") }, ids);
            Assert.True(r.TryGetVec2("v", out var v));
            Assert.Equal(new Vec2(3, 4), v);
            Assert.True(r.TryGetObject("o", out var o));
            Assert.Empty(o);
            Assert.True(r.TryGetArray("a", out var a));
            Assert.Empty(a);
        }
    }
}
