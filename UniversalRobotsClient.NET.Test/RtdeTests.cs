// Tests for RTDE DataPackage and RtdeValue

using System.Collections.Generic;
using UniversalRobotics.Rtde.Packets;

namespace UniversalRobotics.Tests
{
    public class RtdeValueTests
    {
        [Fact]
        public void FromBool_TryGetBool_RoundTrips()
        {
            var v = RtdeValue.FromBool(true);
            Assert.Equal(RtdeValueKind.Bool, v.Kind);
            Assert.True(v.TryGetBool(out bool result));
            Assert.True(result);
        }

        [Fact]
        public void FromDouble_TryGetDouble_RoundTrips()
        {
            var v = RtdeValue.FromDouble(3.14);
            Assert.Equal(RtdeValueKind.Double, v.Kind);
            Assert.True(v.TryGetDouble(out double result));
            Assert.Equal(3.14, result, precision: 10);
        }

        [Fact]
        public void FromVector6d_TryGetVector6d_RoundTrips()
        {
            var original = new double[] { 1, 2, 3, 4, 5, 6 };
            var v = RtdeValue.FromVector6d(original);
            Assert.Equal(RtdeValueKind.Vector6d, v.Kind);
            Assert.True(v.TryGetVector6d(out var result));
            Assert.Equal(original, result);
        }

        [Fact]
        public void WrongTypeGet_ReturnsFalse()
        {
            var v = RtdeValue.FromBool(true);
            Assert.False(v.TryGetDouble(out _));
        }

        [Fact]
        public void FromVector6d_ClonesArray()
        {
            var arr = new double[] { 1, 2, 3, 4, 5, 6 };
            var v = RtdeValue.FromVector6d(arr);
            arr[0] = 999;
            v.TryGetVector6d(out var result);
            Assert.Equal(1.0, result![0]); // clone is unaffected
        }
    }

    public class DataPackageTests
    {
        [Fact]
        public void SetData_GetData_Bool_RoundTrips()
        {
            var recipe = new List<string> { "output_bit_register_64" };
            var types = new List<string> { "BOOL" };
            var pkg = new DataPackage(recipe, types);

            Assert.True(pkg.SetData("output_bit_register_64", true));
            Assert.True(pkg.GetData<bool>("output_bit_register_64", out bool val));
            Assert.True(val);
        }

        [Fact]
        public void SetData_GetData_Double_RoundTrips()
        {
            var recipe = new List<string> { "timestamp" };
            var types = new List<string> { "DOUBLE" };
            var pkg = new DataPackage(recipe, types);

            Assert.True(pkg.SetData("timestamp", 1234.5678));
            Assert.True(pkg.GetData<double>("timestamp", out double val));
            Assert.Equal(1234.5678, val, precision: 10);
        }

        [Fact]
        public void GetData_MissingField_ReturnsFalse()
        {
            var pkg = new DataPackage(new List<string> { "timestamp" });
            Assert.False(pkg.GetData<double>("nonexistent", out _));
        }

        [Fact]
        public void SetData_MissingField_ReturnsFalse()
        {
            var pkg = new DataPackage(new List<string> { "timestamp" });
            Assert.False(pkg.SetData("nonexistent", 1.0));
        }

        [Fact]
        public void Recipe_ContainsAllFields()
        {
            var recipe = new List<string> { "timestamp", "actual_q", "target_q" };
            var pkg = new DataPackage(recipe);
            Assert.Equal(3, pkg.Recipe.Count);
        }
    }

    public class RtdeTypeMapTests
    {
        [Theory]
        [InlineData("BOOL", RtdeValueKind.Bool)]
        [InlineData("DOUBLE", RtdeValueKind.Double)]
        [InlineData("UINT32", RtdeValueKind.UInt32)]
        [InlineData("VECTOR6D", RtdeValueKind.Vector6d)]
        [InlineData("VECTOR3D", RtdeValueKind.Vector3d)]
        [InlineData("NOT_FOUND", RtdeValueKind.None)]
        [InlineData("IN_USE", RtdeValueKind.None)]
        [InlineData("UNKNOWNTYPE", RtdeValueKind.None)]
        public void Resolve_KnownTypes_ReturnsCorrectKind(string typeName, RtdeValueKind expected)
            => Assert.Equal(expected, RtdeTypeMap.Resolve(typeName));
    }
}
