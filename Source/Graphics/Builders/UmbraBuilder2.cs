﻿using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Penumbra.Mathematics;
using Penumbra.Mathematics.Collision;
using Penumbra.Utilities;

namespace Penumbra.Graphics.Builders
{
    internal class UmbraBuilder2
    {
        private readonly HullList _hulls;

        private readonly List<int> _hullIndices = new List<int>();
        private readonly Polygon _hullVertices = new Polygon();

        private readonly FastList<Vector2> _vertices = new FastList<Vector2>();
        private readonly FastList<int> _indices = new FastList<int>();

        private int _indexOffset;

        public UmbraBuilder2(HullList hulls)
        {
            _hulls = hulls;
        }

        public void PreProcess()
        {
            _indexOffset = 0;
            _vertices.Clear();
            _indices.Clear();
        }


        private readonly List<List<HullPointContext>> _segments = new List<List<HullPointContext>>();
        public void ProcessHull(Light light, Hull hull, ref HullContext hullCtx)
        {
            PopulateSegments(light, hull, ref hullCtx);
            PopulateVertices(light, ref hullCtx);            
            _segments.Clear();
        }        

        private void PopulateSegments(Light light, Hull hull, ref HullContext hullCtx, int lowerLimit = -1, int upperLimit = -1)
        {
            // FIND FIRST RIGHT WHICH IS NOT OVERLAPPED BE PREVIOUS RIGHT

            // FIND FIRST RIGHT
            var points = hullCtx.PointContexts;
            int count = points.Count;
            bool isClosed = _segments.Count == 0;
            foreach (int i in points.GetIndicesBetween(isClosed ? 0 : lowerLimit, isClosed ? count - 1 : upperLimit))
            {
                var ctx = hullCtx.PointContexts[i];
                if (ctx.RightSide == Side.Right)
                {
                    var startIndex = i;
                    if (!hullCtx.IsConvex || !isClosed && i != lowerLimit)
                    {
                        // FIND PREVIOUS RIGHT TO DETERMINE WHICH ONE COMES FIRST / IF WE NEED TO MERGE
                        //for (int j = (startIndex - 1) < 0 ? count - 1 : startIndex - 1; j != i; j = (j - 1) < 0 ? count - 1 : j - 1)
                        foreach (int j in points.GetIndicesBetweenBackward(points.PreviousIndex(startIndex), isClosed ? points.NextIndex(startIndex) : lowerLimit))
                        {
                            var prevCtx = hullCtx.PointContexts[j];
                            if (prevCtx.LeftSide == Side.Left)
                            {
                                if (VectorUtil.IsADirectingRightFromB(ref ctx.LightRightToPointDir, ref prevCtx.LightLeftToPointDir))
                                {
                                    break;
                                }
                            }
                            else if (prevCtx.RightSide == Side.Right)
                            {
                                if (VectorUtil.IsADirectingRightFromB(ref prevCtx.LightRightToPointDir, ref ctx.LightRightToPointDir))
                                {
                                    startIndex = j;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }

                    // FIND LEFT NEXT FROM RIGHT
                    int endIndex = 0;                    
                    //for (int j = (i + 1) % count; j != i; j = (j + 1) % count)
                    int nextIndex = points.NextIndex(i);
                    if (nextIndex != upperLimit)
                    { 
                        foreach (int j in points.GetIndicesBetween(points.NextIndex(i), isClosed ? points.PreviousIndex(i) : upperLimit))
                        {
                            var nextCtx = hullCtx.PointContexts[j];
                            if (nextCtx.LeftSide == Side.Left || !isClosed && j != upperLimit)
                            {
                                endIndex = j;
                                // FIND NEXT LEFT TO DETERMINE WHICH ONE COMES FIRST / IF WE NEED TO MERGE
                                //for (int k = (endIndex + 1) % count; k != j; k = (k + 1) % count)
                                foreach (int k in points.GetIndicesBetween(points.NextIndex(endIndex), isClosed ? points.PreviousIndex(endIndex) : upperLimit))
                                {
                                    var nextCtx2 = hullCtx.PointContexts[k];
                                    if (nextCtx2.RightSide == Side.Right)
                                    {
                                        if (VectorUtil.IsADirectingRightFromB(ref nextCtx2.LightRightToPointDir, ref nextCtx.LightLeftToPointDir))
                                        {
                                            break;                                        
                                        }
                                    }
                                    else if (nextCtx2.LeftSide == Side.Left)
                                    {
                                        if (!VectorUtil.IsADirectingRightFromB(ref nextCtx2.LightLeftToPointDir, ref nextCtx.LightLeftToPointDir))
                                        {
                                            endIndex = k;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }

                    var segment = new List<HullPointContext>();
                    _segments.Add(segment);
                    foreach (int j in points.GetIndicesBetween(startIndex, endIndex))
                    {
                        segment.Add(points[j]);
                    }

                    for (int j = 0; j < _segments.Count; j++)
                    {
                        var seg = _segments[j];
                        var nextSeg = _segments.NextElement(j);                                                
                        var newStartPrev = seg[seg.Count - 1];
                        var newEndNext = nextSeg[0];

                        var nextStart = points.NextIndex(newStartPrev.Index);
                        var newEnd = points.PreviousIndex(newEndNext.Index);                        
                        if (nextStart != newEndNext.Index && newEnd != newStartPrev.Index && nextStart != newEnd)
                        {
                            PopulateSegments(light, hull, ref hullCtx, nextStart, newEnd);
                        }                        
                    }

                    break;
                } // foreach ends here 
            }
        }

        private void PopulateVertices(Light light, ref HullContext hullCtx)
        {
            foreach (var segment in _segments)
            {
                if (segment.Count <= 1) continue;

                // NEXT PHASE

                var right = segment[0];
                var left = segment[segment.Count - 1];
                var line1 = new Line2D(right.LightRight, right.LightRight + right.LightRightToPointDir);
                var line2 = new Line2D(left.LightLeft, left.LightLeft + left.LightLeftToPointDir);

                Vector2 intersectionPos;
                var linesIntersect = line1.Intersects(ref line2, out intersectionPos);

                var midDir = linesIntersect
                    ? Vector2.Normalize(intersectionPos - light.Position)
                    : Vector2.Normalize(right.LightRightToPointDir + left.LightLeftToPointDir);

                if (Vector2.Dot(midDir, right.LightRightToPointDir) < 0)
                    midDir *= -1;

                var pointOnRange = light.Position + midDir * light.Range;

                var areIntersectingInFrontOfLight = Vector2.DistanceSquared(intersectionPos, pointOnRange) <
                                                    light.RangeSquared;

                var tangentDir = VectorUtil.Rotate90CW(midDir);

                var tangentLine = new Line2D(pointOnRange, pointOnRange + tangentDir);

                Vector2 projectedPoint1;
                tangentLine.Intersects(ref line1, out projectedPoint1);
                Vector2 projectedPoint2;
                tangentLine.Intersects(ref line2, out projectedPoint2);

                if (linesIntersect && areIntersectingInFrontOfLight)
                {
                    var areIntersectingInsideLightRange = Vector2.DistanceSquared(intersectionPos, light.Position) <
                                                          light.RangeSquared;

                    if (areIntersectingInsideLightRange)
                    {
                        hullCtx.UmbraIntersectionType = IntersectionType.IntersectsInsideLight;
                        _hullVertices.Add(intersectionPos);
                    }
                    else
                    {
                        hullCtx.UmbraIntersectionType = IntersectionType.IntersectsOutsideLight;
                        _hullVertices.Add(projectedPoint1);
                        _hullVertices.Add(projectedPoint2);
                    }

                    hullCtx.UmbraIntersectionPoint = intersectionPos;
                    hullCtx.UmbraRightProjectedPoint = projectedPoint1;
                    hullCtx.UmbraLeftProjectedPoint = projectedPoint2;
                }
                else
                {
                    _hullVertices.Add(projectedPoint1);
                    _hullVertices.Add(projectedPoint2);
                }


                // Add all the vertices that contain the segment on the hull.
                var numSegmentVertices = segment.Count;
                for (var i = numSegmentVertices - 1; i >= 0; i--)
                {
                    var point = segment[i].Point;
                    _hullVertices.Add(point);
                }

                //Vector2[] outVertices;
                //int[] outIndices;
                //Triangulator.Triangulate(vertices.ToArray(), WindingOrder.CounterClockwise,
                //    WindingOrder.CounterClockwise, WindingOrder.Clockwise, out outVertices,
                //    out outIndices);                            
                _hullVertices.GetIndices(WindingOrder.Clockwise, _hullIndices);

                _vertices.AddRange(_hullVertices);
                for (var i = 0; i < _hullIndices.Count; i++)
                {
                    _hullIndices[i] = _hullIndices[i] + _indexOffset;
                }
                _indices.AddRange(_hullIndices);
                _indexOffset += _hullVertices.Count;

                _hullVertices.Clear();
                _hullIndices.Clear();
            }
        }

        public void Build(Light light, LightVaos vaos)
        {
            if (_vertices.Count > 0 && _indices.Count > 0)
            {
                vaos.HasUmbra = true;
                vaos.UmbraVao.SetVertices(_vertices);
                vaos.UmbraVao.SetIndices(_indices);
            }
            else
            {
                vaos.HasUmbra = false;
            }
        }
    }
}
