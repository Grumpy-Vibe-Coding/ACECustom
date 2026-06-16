using System;
using System.Collections.Generic;
using System.Numerics;
using ACE.Entity;
using ACE.Server.Entity;
using Position = ACE.Entity.Position;

namespace ACE.Server.Entity
{
    public static class PoissonDiskSampler
    {
        public static List<Position> GeneratePoints(Position center, float radius, float minDist, Func<Position, bool> isValidPredicate)
        {
            var points = new List<Position>();
            var activeList = new List<Vector2>();

            // Bounding box of the generator radius clamped to the landblock
            float minX = Math.Max(0.5f, center.PositionX - radius);
            float maxX = Math.Min(191.5f, center.PositionX + radius);
            float minY = Math.Max(0.5f, center.PositionY - radius);
            float maxY = Math.Min(191.5f, center.PositionY + radius);

            float radiusSq = radius * radius;
            float minDistSq = minDist * minDist;

            // Grid cell size
            float cellSize = minDist / 1.4142135f; // r / sqrt(2)
            int cols = (int)Math.Ceiling(192.0f / cellSize);
            int rows = (int)Math.Ceiling(192.0f / cellSize);

            // -1 indicates empty cell
            var grid = new int[cols, rows];
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    grid[x, y] = -1;

            var random = new Random((int)center.LandblockId.Raw);

            // Helper to get grid cell indices
            (int col, int row) GetGridCell(Vector2 p)
            {
                int c = (int)(p.X / cellSize);
                int r = (int)(p.Y / cellSize);
                return (Math.Clamp(c, 0, cols - 1), Math.Clamp(r, 0, rows - 1));
            }

            // Try to find a valid starting point near center
            Vector2? firstPoint = null;
            for (int tryStart = 0; tryStart < 20; tryStart++)
            {
                float dx = (float)(random.NextDouble() * 2 - 1) * radius * 0.2f;
                float dy = (float)(random.NextDouble() * 2 - 1) * radius * 0.2f;
                var testPt = new Vector2(center.PositionX + dx, center.PositionY + dy);

                // Clamp to landblock bounds
                testPt.X = Math.Clamp(testPt.X, minX, maxX);
                testPt.Y = Math.Clamp(testPt.Y, minY, maxY);

                var posObj = new Position(center);
                posObj.PositionX = testPt.X;
                posObj.PositionY = testPt.Y;
                posObj.PositionZ = posObj.GetTerrainZ() + 0.05f;
                posObj.LandblockId = new LandblockId(posObj.GetCell());

                if (isValidPredicate(posObj))
                {
                    firstPoint = testPt;
                    points.Add(posObj);
                    var cell = GetGridCell(testPt);
                    grid[cell.col, cell.row] = 0;
                    activeList.Add(testPt);
                    break;
                }
            }

            if (firstPoint == null)
            {
                // If we couldn't even find a start point, return empty list
                return points;
            }

            int k = 30; // Max candidate attempts per point

            while (activeList.Count > 0)
            {
                int activeIndex = random.Next(activeList.Count);
                Vector2 parentPt = activeList[activeIndex];
                bool foundCandidate = false;

                for (int i = 0; i < k; i++)
                {
                    // Generate point in annulus between r and 2r around parent
                    double angle = random.NextDouble() * 2 * Math.PI;
                    float dist = minDist + (float)random.NextDouble() * minDist;
                    var candidate = new Vector2(
                        parentPt.X + (float)Math.Cos(angle) * dist,
                        parentPt.Y + (float)Math.Sin(angle) * dist
                    );

                    // Bounding checks
                    if (candidate.X < minX || candidate.X > maxX || candidate.Y < minY || candidate.Y > maxY)
                        continue;

                    // Circle check vs generator center
                    float distFromCenterSq = Vector2.DistanceSquared(candidate, new Vector2(center.PositionX, center.PositionY));
                    if (distFromCenterSq > radiusSq)
                        continue;

                    // Neighborhood check in grid
                    var cell = GetGridCell(candidate);
                    bool tooClose = false;

                    // Scan neighboring cells: 5x5 area around the target cell
                    int minCol = Math.Max(0, cell.col - 2);
                    int maxCol = Math.Min(cols - 1, cell.col + 2);
                    int minRow = Math.Max(0, cell.row - 2);
                    int maxRow = Math.Min(rows - 1, cell.row + 2);

                    for (int c = minCol; c <= maxCol && !tooClose; c++)
                    {
                        for (int r = minRow; r <= maxRow && !tooClose; r++)
                        {
                            int otherIdx = grid[c, r];
                            if (otherIdx != -1)
                            {
                                var otherPos = points[otherIdx];
                                var otherVec = new Vector2(otherPos.PositionX, otherPos.PositionY);
                                if (Vector2.DistanceSquared(candidate, otherVec) < minDistSq)
                                {
                                    tooClose = true;
                                }
                            }
                        }
                    }

                    if (tooClose)
                        continue;

                    // Walkability/Terrain check
                    var posObj = new Position(center);
                    posObj.PositionX = candidate.X;
                    posObj.PositionY = candidate.Y;
                    posObj.PositionZ = posObj.GetTerrainZ() + 0.05f;
                    posObj.LandblockId = new LandblockId(posObj.GetCell());

                    if (isValidPredicate(posObj))
                    {
                        points.Add(posObj);
                        grid[cell.col, cell.row] = points.Count - 1;
                        activeList.Add(candidate);
                        foundCandidate = true;
                        break;
                    }
                }

                if (!foundCandidate)
                {
                    activeList.RemoveAt(activeIndex);
                }
            }

            return points;
        }
    }
}
