﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows;

using ControllerCNC.Primitives;

namespace ControllerCNC.Planning
{
    public class PathTracer2D
    {
        /// <summary>
        /// Velocity at end of the planned path.
        /// </summary>
        private Vector _actualVelocity = new Vector(0, 0);

        private Vector _actualPosition = new Vector(0, 0);

        private List<Plan> _pathPlansX = new List<Plan>();
        private List<Plan> _pathPlansY = new List<Plan>();

        public void AppendAcceleration(Vector acceleration, double time)
        {
            var initialVelocity = _actualVelocity;

            var startPosition = _actualPosition;
            var newPosition = _actualPosition + _actualVelocity * time + 0.5 * acceleration * time * time;
            var newVelocity = _actualVelocity + acceleration * time;

            var distance = newPosition - startPosition;

            if (Math.Sign(initialVelocity.X) * Math.Sign(newVelocity.X) < 0)
            {
                var stopTime = Math.Abs(initialVelocity.X / acceleration.X);
                AppendAcceleration(acceleration, stopTime);
                AppendAcceleration(acceleration, time - stopTime);
                return;
            }

            if (Math.Sign(initialVelocity.Y) * Math.Sign(newVelocity.Y) < 0)
            {
                var stopTime = Math.Abs(initialVelocity.Y / acceleration.Y);
                AppendAcceleration(acceleration, stopTime);
                AppendAcceleration(acceleration, time - stopTime);
                return;
            }

            _actualPosition = newPosition;
            _actualVelocity = newVelocity;
            //TODO increase precision with integer clipping
            addRampPlan(initialVelocity.X, newVelocity.X, time, distance.X, _pathPlansX);
            addRampPlan(initialVelocity.Y, newVelocity.Y, time, distance.Y, _pathPlansY);
        }


        public void Continue(double time)
        {
            var startPosition = _actualPosition;
            _actualPosition = _actualPosition + _actualVelocity * time;

            var endPosition = _actualPosition;
            var distance = endPosition - startPosition;
            var tickCount = (int)(DriverCNC.TimeScale * time);
            addConstantPlan(_actualVelocity.X, tickCount, distance.X, _pathPlansX);
            addConstantPlan(_actualVelocity.Y, tickCount, distance.Y, _pathPlansY);
        }

        internal void Execute(DriverCNC cnc)
        {
            for (var i = 0; i < _pathPlansX.Count; ++i)
            {
                var planX = _pathPlansX[i];
                var planY = _pathPlansY[i];

                System.Diagnostics.Debug.WriteLine("PathTracer");
                System.Diagnostics.Debug.WriteLine("\tX: " + planX);
                System.Diagnostics.Debug.WriteLine("\tY: " + planY);

                cnc.StepperIndex = 2;
                //TODO add polymorphism
                if (planX is AccelerationPlan)
                {
                    cnc.SEND((AccelerationPlan)planX);
                    cnc.SEND((AccelerationPlan)planY);
                }
                else
                {
                    cnc.SEND((ConstantPlan)planX);
                    cnc.SEND((ConstantPlan)planY);
                }
            }
        }

        private void addConstantPlan(double velocity, int tickCount, double distance, List<Plan> pathPlans)
        {
            checked
            {
                //fraction is clipped because period can be used for remainder
                var stepCount = (Int16)distance;
                if (stepCount == 0)
                {
                    pathPlans.Add(new ConstantPlan(0, 0, 0, 0));
                    return;
                }
                checked
                {
                    var baseDeltaExact = Math.Abs(DriverCNC.TimeScale / velocity);
                    var baseDelta = Math.Abs((int)(baseDeltaExact));
                    var periodDenominator = (UInt16)Math.Abs(stepCount);
                    var tickRemainder = (UInt16)(tickCount - periodDenominator * baseDelta);
                    var constantPlan = new ConstantPlan(stepCount, baseDelta, tickRemainder, periodDenominator);
                    pathPlans.Add(constantPlan);
                }
            }
        }

        private void addRampPlan(double initialSpeed, double endSpeed, double exactDuration, double distance, List<Plan> pathPlans)
        {
            var tickCount = exactDuration * DriverCNC.TimeScale;
            if ((int)tickCount <= 0)
                //nothing to add here
                return;
            checked
            {
                var profile = findAccelerationProfile(initialSpeed, endSpeed, Math.Abs(distance), exactDuration);

                var startN = profile.IsDeceleration ? -profile.InitialN : profile.InitialN;
                var accelerationPlan = new AccelerationPlan((Int16)distance, profile.StartDeltaT, profile.BaseDelta, profile.BaseRemainder, startN);
                var timeDiff = Math.Abs(profile.TotalTickCount - exactDuration * DriverCNC.TimeScale);
                System.Diagnostics.Debug.WriteLine("Acceleration time diff: " + timeDiff);
                System.Diagnostics.Debug.WriteLine("\t" + profile);
                pathPlans.Add(accelerationPlan);
            }
        }

        private AccelerationProfile findAccelerationProfile(double initialSpeed, double endSpeed, double distance, double exactDuration)
        {
            var absoluteInitialSpeed = Math.Abs(initialSpeed);
            var absoluteEndSpeed = Math.Abs(endSpeed);
            var absoluteStepCount = (int)Math.Abs(distance);
            var tickCount = (int)(exactDuration * DriverCNC.TimeScale);

            var acceleration = Math.Abs(absoluteEndSpeed - absoluteInitialSpeed) / exactDuration;
            var rawC0 = DriverCNC.TimeScale * Math.Sqrt(2 / acceleration);

            var isDeceleration = absoluteInitialSpeed > absoluteEndSpeed;

            var targetDelta = (int)Math.Abs(Math.Round(DriverCNC.TimeScale / endSpeed));
            if (targetDelta < 0)
                //overflow when decelerating to stand still
                targetDelta = int.MaxValue;

            if (isDeceleration)
                rawC0 = -rawC0;
            var plan = new AccelerationProfile(rawC0, targetDelta, absoluteStepCount, tickCount);

            return plan;
        }
    }
}