// Tests for core data types — mirrors C++ test_datatypes.cpp

using System;
using UniversalRobotics.Models;
using UniversalRobotics.Protocol;

namespace UniversalRobotics.Tests
{
    public class JointAnglesTests
    {
        [Fact]
        public void Constructor_SixDoubles_SetsValues()
        {
            var q = new JointAngles(1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
            Assert.Equal(1.0, q.Q1);
            Assert.Equal(6.0, q.Q6);
        }

        [Fact]
        public void Constructor_Array_ClonesInput()
        {
            double[] arr = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6 };
            var q = new JointAngles(arr);
            arr[0] = 999; // mutate original
            Assert.Equal(0.1, q.Q1); // clone should be unaffected
        }

        [Fact]
        public void Constructor_WrongLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JointAngles(new double[] { 1.0, 2.0 }));
        }

        [Fact]
        public void ToArray_ReturnsCopy()
        {
            var q = new JointAngles(1, 2, 3, 4, 5, 6);
            var arr = q.ToArray();
            arr[0] = 999;
            Assert.Equal(1.0, q.Q1); // original unaffected
        }

        [Fact]
        public void Indexer_ReturnsCorrectJoint()
        {
            var q = new JointAngles(10, 20, 30, 40, 50, 60);
            Assert.Equal(10.0, q[0]);
            Assert.Equal(60.0, q[5]);
        }

        [Fact]
        public void Equals_SameValues_ReturnsTrue()
        {
            var a = new JointAngles(1, 2, 3, 4, 5, 6);
            var b = new JointAngles(1, 2, 3, 4, 5, 6);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Equals_DifferentValues_ReturnsFalse()
        {
            var a = new JointAngles(1, 2, 3, 4, 5, 6);
            var b = new JointAngles(1, 2, 3, 4, 5, 7);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void GetHashCode_EqualObjects_HaveSameHash()
        {
            var a = new JointAngles(1, 2, 3, 4, 5, 6);
            var b = new JointAngles(1, 2, 3, 4, 5, 6);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }

    public class CartesianPoseTests
    {
        [Fact]
        public void Constructor_SetsAllFields()
        {
            var p = new CartesianPose(0.1, 0.2, 0.3, 0.01, 0.02, 0.03);
            Assert.Equal(0.1, p.X);
            Assert.Equal(0.2, p.Y);
            Assert.Equal(0.3, p.Z);
            Assert.Equal(0.01, p.Rx);
            Assert.Equal(0.02, p.Ry);
            Assert.Equal(0.03, p.Rz);
        }

        [Fact]
        public void QNear_NullByDefault()
        {
            var p = new CartesianPose(0, 0, 0, 0, 0, 0);
            Assert.Null(p.QNear);
        }

        [Fact]
        public void QNear_CanBeSet()
        {
            var q = new JointAngles(1, 2, 3, 4, 5, 6);
            var p = new CartesianPose(0, 0, 0, 0, 0, 0, q);
            Assert.Equal(q, p.QNear);
        }

        [Fact]
        public void ToArray_ReturnsCorrectElements()
        {
            var p = new CartesianPose(1, 2, 3, 4, 5, 6);
            var arr = p.ToArray();
            Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, arr);
        }

        [Fact]
        public void Equals_SameValues_ReturnsTrue()
        {
            var a = new CartesianPose(1, 2, 3, 4, 5, 6);
            var b = new CartesianPose(1, 2, 3, 4, 5, 6);
            Assert.Equal(a, b);
        }
    }

    public class VersionInformationTests
    {
        [Theory]
        [InlineData("5.12.0.1101319", 5u, 12u, 0u, 1101319u)]
        [InlineData("3.14.3", 3u, 14u, 3u, 0u)]
        [InlineData("10.7.0", 10u, 7u, 0u, 0u)]
        public void Parse_ValidString_ReturnsCorrectVersion(string input, uint major, uint minor, uint bugfix, uint build)
        {
            var v = VersionInformation.Parse(input);
            Assert.Equal(major, v.Major);
            Assert.Equal(minor, v.Minor);
            Assert.Equal(bugfix, v.Bugfix);
            Assert.Equal(build, v.Build);
        }

        [Fact]
        public void Parse_WithLeadingV_Succeeds()
        {
            var v = VersionInformation.Parse("v5.23.0");
            Assert.Equal(5u, v.Major);
            Assert.Equal(23u, v.Minor);
        }

        [Fact]
        public void IsEmpty_AllZeros_ReturnsTrue()
        {
            var v = new VersionInformation();
            Assert.True(v.IsEmpty);
        }

        [Fact]
        public void IsEmpty_NonZero_ReturnsFalse()
        {
            var v = new VersionInformation(5, 0, 0, 0);
            Assert.False(v.IsEmpty);
        }

        [Fact]
        public void IsESeries_Major5_ReturnsTrue()
        {
            var v = new VersionInformation(5, 12, 0, 0);
            Assert.True(v.IsESeries);
        }

        [Fact]
        public void IsPolyScopeX_Major10_ReturnsTrue()
        {
            var v = new VersionInformation(10, 7, 0, 0);
            Assert.True(v.IsPolyScopeX);
        }

        [Fact]
        public void Comparison_Operators_Work()
        {
            var v1 = new VersionInformation(5, 21, 0, 0);
            var v2 = new VersionInformation(5, 23, 0, 0);
            Assert.True(v1 < v2);
            Assert.True(v2 > v1);
            Assert.True(v1 <= v1);
            Assert.True(v1 >= v1);
            Assert.True(v1 != v2);
            Assert.True(v1 == VersionInformation.Parse("5.21.0"));
        }

        [Fact]
        public void TryParse_InvalidString_ReturnsFalse()
        {
            Assert.False(VersionInformation.TryParse("not-a-version", out _));
        }
    }

    public class ControlModeTests
    {
        [Fact]
        public void IsRealtime_ServoJ_ReturnsTrue()
            => Assert.True(ControlModeTypes.IsRealtime(ControlMode.ServoJ));

        [Fact]
        public void IsRealtime_Idle_ReturnsFalse()
            => Assert.False(ControlModeTypes.IsRealtime(ControlMode.Idle));

        [Fact]
        public void IsNonRealtime_Forward_ReturnsTrue()
            => Assert.True(ControlModeTypes.IsNonRealtime(ControlMode.Forward));

        [Fact]
        public void IsStationary_Stopped_ReturnsTrue()
            => Assert.True(ControlModeTypes.IsStationary(ControlMode.Stopped));

        [Theory]
        [InlineData(ControlMode.Stopped, -2)]
        [InlineData(ControlMode.Uninitialized, -1)]
        [InlineData(ControlMode.Idle, 0)]
        [InlineData(ControlMode.ServoJ, 1)]
        [InlineData(ControlMode.SpeedJ, 2)]
        [InlineData(ControlMode.Forward, 3)]
        [InlineData(ControlMode.SpeedL, 4)]
        [InlineData(ControlMode.Pose, 5)]
        [InlineData(ControlMode.Freedrive, 6)]
        [InlineData(ControlMode.ToolInContact, 7)]
        [InlineData(ControlMode.Torque, 8)]
        public void ControlMode_NumericValues_MatchProtocol(ControlMode mode, int expected)
            => Assert.Equal(expected, (int)mode);
    }

    public class MotionTargetTests
    {
        [Fact]
        public void JointTarget_IsJoint_True_IsCartesian_False()
        {
            MotionTarget t = new JointAngles(1, 2, 3, 4, 5, 6);
            Assert.True(t.IsJoint);
            Assert.False(t.IsCartesian);
        }

        [Fact]
        public void CartesianTarget_IsCartesian_True_IsJoint_False()
        {
            MotionTarget t = new CartesianPose(0.1, 0.2, 0.3, 0, 0, 0);
            Assert.True(t.IsCartesian);
            Assert.False(t.IsJoint);
        }

        [Fact]
        public void ImplicitConversion_FromJointAngles_Works()
        {
            var q = new JointAngles(0, 0, 0, 0, 0, 0);
            MotionTarget t = q;
            Assert.Same(q, t.JointTarget);
        }
    }
}
