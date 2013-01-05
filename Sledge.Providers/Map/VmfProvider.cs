﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Sledge.Common;
using Sledge.DataStructures.Geometric;
using Sledge.DataStructures.MapObjects;

namespace Sledge.Providers.Map
{
    public class VmfProvider : MapProvider
    {
        protected override bool IsValidForFileName(string filename)
        {
            return filename.EndsWith(".vmf");
        }

        private static long GetObjectID(GenericStructure gs, IDGenerator generator)
        {
            var id = gs.PropertyLong("id");
            if (id == 0) id = generator.GetNextObjectID();
            return id;
        }

        private static void FlattenTree(MapObject parent, List<Solid> solids, List<Entity> entities, List<Group> groups)
        {
            foreach (var mo in parent.Children)
            {
                if (mo is Solid)
                {
                    solids.Add((Solid) mo);
                }
                else if (mo is Entity)
                {
                    entities.Add((Entity) mo);
                }
                else if (mo is Group)
                {
                    groups.Add((Group) mo);
                    FlattenTree(mo, solids, entities, groups);
                }
            }
        }

        private static string FormatCoordinate(Coordinate c)
        {
            return c.X.ToString("0.000") + " " + c.Y.ToString("0.000") + " " + c.Z.ToString("0.000");
        }

        private static string FormatColor(Color c)
        {
            return c.R + " " + c.G + " " + c.B;
        }

        private static EntityData ReadEntityData(GenericStructure structure)
        {
            var ret = new EntityData();
            foreach (var key in structure.GetPropertyKeys())
            {
                ret.Properties.Add(new Property {Key = key, Value = structure[key]});
            }
            ret.Flags = structure.PropertyInteger("spawnflags");
            return ret;
        }

        private static void WriteEntityData(GenericStructure obj, EntityData data)
        {
            foreach (var property in data.Properties)
            {
                obj[property.Key] = property.Value;
            }
            obj["spawnflags"] = data.Flags.ToString();
        }

        private static GenericStructure WriteEditor(MapObject obj)
        {
            var editor = new GenericStructure("editor");
            editor["color"] = FormatColor(obj.Colour);
            foreach (var visgroup in obj.Visgroups)
            {
                editor.AddProperty("visgroupid", visgroup.ToString());
            }
            editor["visgroupshown"] = "1";
            editor["visgroupautoshown"] = "1";
            if (obj.Parent is Group)
            {
                editor["groupid"] = ((Group)obj.Parent).ID.ToString();
            }
            return editor;
        }

        private static Displacement ReadDisplacement(long id, GenericStructure dispinfo)
        {
            var disp = new Displacement(id);
            // power, startposition, flags, elevation, subdiv, normals{}, distances{},
            // offsets{}, offset_normals{}, alphas{}, triangle_tags{}, allowed_verts{}
            disp.SetPower(dispinfo.PropertyInteger("power", 3));
            disp.StartPosition = dispinfo.PropertyCoordinate("startposition");
            disp.Elevation = dispinfo.PropertyDecimal("elevation");
            disp.SubDiv = dispinfo.PropertyInteger("subdiv") > 0;
            var size = disp.Resolution + 1;
            var normals = dispinfo.GetChildren("normals").FirstOrDefault();
            var distances = dispinfo.GetChildren("distances").FirstOrDefault();
            var offsets = dispinfo.GetChildren("offsets").FirstOrDefault();
            var offsetNormals = dispinfo.GetChildren("offset_normals").FirstOrDefault();
            var alphas = dispinfo.GetChildren("alphas").FirstOrDefault();
            //var triangleTags = dispinfo.GetChildren("triangle_tags").First();
            //var allowedVerts = dispinfo.GetChildren("allowed_verts").First();
            for (var i = 0; i < size; i++)
            {
                var row = "row" + i;
                var norm = normals != null ? normals.PropertyCoordinateArray(row, size) : Enumerable.Range(0, size).Select(x => Coordinate.Zero).ToArray();
                var dist = distances != null ? distances.PropertyDecimalArray(row, size) : Enumerable.Range(0, size).Select(x => 0m).ToArray();
                var offn = offsetNormals != null ? offsetNormals.PropertyCoordinateArray(row, size) : Enumerable.Range(0, size).Select(x => Coordinate.Zero).ToArray();
                var offs = offsets != null ? offsets.PropertyDecimalArray(row, size) : Enumerable.Range(0, size).Select(x => 0m).ToArray();
                var alph = alphas != null ? alphas.PropertyDecimalArray(row, size) : Enumerable.Range(0, size).Select(x => 0m).ToArray();
                for (var j = 0; j < size; j++)
                {
                    disp.Points[i, j].Displacement = new Vector(norm[j], dist[j]);
                    disp.Points[i, j].OffsetDisplacement = new Vector(offn[j], offs[j]);
                    disp.Points[i, j].Alpha = alph[j];
                }
            }
            return disp;
        }

        private static GenericStructure WriteDisplacement(Displacement disp)
        {
            throw new NotImplementedException();
        }

        private static Face ReadFace(GenericStructure side, IDGenerator generator)
        {
            var id = side.PropertyLong("id");
            if (id == 0) id = generator.GetNextFaceID();
            var dispinfo = side.GetChildren("dispinfo").FirstOrDefault();
            var ret = dispinfo != null ? ReadDisplacement(id, dispinfo) : new Face(id);
            // id, plane, material, uaxis, vaxis, rotation, lightmapscale, smoothing_groups
            var uaxis = side.PropertyTextureAxis("uaxis");
            var vaxis = side.PropertyTextureAxis("vaxis");
            ret.Texture.Name = side["material"];
            ret.Texture.UAxis = uaxis.Item1;
            ret.Texture.XShift = uaxis.Item2;
            ret.Texture.XScale = uaxis.Item3;
            ret.Texture.VAxis = vaxis.Item1;
            ret.Texture.YShift = vaxis.Item2;
            ret.Texture.YScale = vaxis.Item3;
            ret.Texture.Rotation = side.PropertyDecimal("rotation");
            ret.Plane = side.PropertyPlane("plane");
            return ret;
        }

        private static GenericStructure WriteFace(Face face)
        {
            var ret = new GenericStructure("side");
            ret["id"] = face.ID.ToString();
            ret["plane"] = String.Format("({0}) ({1}) ({2})",
                                         FormatCoordinate(face.Vertices[0].Location),
                                         FormatCoordinate(face.Vertices[1].Location),
                                         FormatCoordinate(face.Vertices[2].Location));
            ret["material"] = face.Texture.Name;
            ret["uaxis"] = String.Format("[{0} {1}] {2}", FormatCoordinate(face.Texture.UAxis), face.Texture.XShift, face.Texture.XScale);
            ret["vaxis"] = String.Format("[{0} {1}] {2}", FormatCoordinate(face.Texture.VAxis), face.Texture.YShift, face.Texture.YScale);
            ret["rotation"] = face.Texture.Rotation.ToString();
            // ret["lightmapscale"]
            // ret["smoothing_groups"]

            var verts = new GenericStructure("vertex");
            for (var i = 0; i < face.Vertices.Count; i++)
            {
                verts["vertex" + i] = FormatCoordinate(face.Vertices[i].Location);
            }
            ret.Children.Add(verts);

            if (face is Displacement)
            {
                ret.Children.Add(WriteDisplacement((Displacement) face));
            }

            return ret;
        }

        private static Solid ReadSolid(GenericStructure solid, IDGenerator generator)
        {
            var editor = solid.GetChildren("editor").FirstOrDefault() ?? new GenericStructure("editor");
            var faces = solid.GetChildren("side").Select(x => ReadFace(x, generator)).ToList();

            var idg = new IDGenerator(); // No need to increment the id generator if it doesn't have to be
            var ret = Solid.CreateFromIntersectingPlanes(faces.Select(x => x.Plane), idg);
            ret.ID = GetObjectID(solid, generator);
            ret.Colour = editor.PropertyColour("color", Colour.GetRandomBrushColour());
            ret.Visgroups.AddRange(editor.GetAllPropertyValues("visgroupid").Select(int.Parse));

            for (var i = 0; i < ret.Faces.Count; i++)
            {
                var face = ret.Faces[i];
                var f = faces.FirstOrDefault(x => x.Plane.Normal.EquivalentTo(ret.Faces[i].Plane.Normal));
                if (f == null)
                {
                    // TODO: Report invalid solids
                    Debug.WriteLine("Invalid solid! ID: " + solid["id"]);
                    return null;
                }
                if (f is Displacement)
                {
                    var disp = (Displacement) f;
                    disp.Plane = face.Plane;
                    disp.Vertices = face.Vertices;
                    disp.AlignTextureToWorld();
                    disp.CalculatePoints();
                    ret.Faces[i] = disp;
                    face = disp;
                }
                face.Texture = f.Texture;
                face.Parent = ret;
                face.Colour = ret.Colour;
                face.UpdateBoundingBox();
            }

            if (ret.Faces.Any(x => x is Displacement))
            {
                ret.Faces.ForEach(x => x.IsHidden = !(x is Displacement));
            }

            ret.UpdateBoundingBox(false);

            return ret;
        }

        private static GenericStructure WriteSolid(Solid solid)
        {
            var ret = new GenericStructure("solid");
            ret["id"] = solid.ID.ToString();

            foreach (var face in solid.Faces)
            {
                ret.Children.Add(WriteFace(face));
            }

            var editor = WriteEditor(solid);
            ret.Children.Add(editor);

            if (solid.IsVisgroupHidden)
            {
                var hidden = new GenericStructure("hidden");
                hidden.Children.Add(ret);
                ret = hidden;
            }

            return ret;
        }

        private static Entity ReadEntity(GenericStructure entity, IDGenerator generator)
        {
            var ret = new Entity(GetObjectID(entity, generator))
                          {
                              ClassName = entity["classname"],
                              EntityData = ReadEntityData(entity),
                              Origin = entity.PropertyCoordinate("origin")
                          };
            var editor = entity.GetChildren("editor").FirstOrDefault() ?? new GenericStructure("editor");
            ret.Colour = editor.PropertyColour("color", Colour.GetRandomBrushColour());
            ret.Visgroups.AddRange(editor.GetAllPropertyValues("visgroupid").Select(int.Parse));
            ret.Children.AddRange(entity.GetChildren("solid").Select(solid => ReadSolid(solid, generator)).Where(s => s != null));
            ret.UpdateBoundingBox(false);
            return ret;
        }

        private static GenericStructure WriteEntity(Entity ent)
        {
            var ret = new GenericStructure("entity");
            ret["id"] = ent.ID.ToString();
            ret["classname"] = ent.EntityData.Name;
            WriteEntityData(ret, ent.EntityData);
            if (ent.Children.Count == 0) ret["origin"] = FormatCoordinate(ent.Origin);

            var editor = WriteEditor(ent);
            ret.Children.Add(editor);

            foreach (var solid in ent.Children.SelectMany(x => x.FindAll()).OfType<Solid>())
            {
                ret.Children.Add(WriteSolid(solid));
            }

            return ret;
        }

        private static Group ReadGroup(GenericStructure group, IDGenerator generator)
        {
            var g = new Group(GetObjectID(group, generator));
            var editor = group.GetChildren("editor").FirstOrDefault() ?? new GenericStructure("editor");
            g.Colour = editor.PropertyColour("color", Colour.GetRandomBrushColour());
            g.Visgroups.AddRange(editor.GetAllPropertyValues("visgroupid").Select(int.Parse));
            return g;
        }

        private static GenericStructure WriteGroup(Group group)
        {
            var ret = new GenericStructure("group");
            ret["id"] = group.ID.ToString();

            var editor = WriteEditor(group);
            ret.Children.Add(editor);

            return ret;
        }

        private static World ReadWorld(GenericStructure world, IDGenerator generator)
        {
            var ret = new World(GetObjectID(world, generator))
                          {
                              ClassName = "worldspawn",
                              EntityData = ReadEntityData(world)
                          };

            // Load groups
            var groups = new Dictionary<Group, long>();
            foreach (var group in world.GetChildren("group"))
            {
                var g = ReadGroup(group, generator);
                var editor = group.GetChildren("editor").FirstOrDefault() ?? new GenericStructure("editor");
                var gid = editor.PropertyLong("groupid");
                groups.Add(g, gid);
            }

            // Build group tree
            var assignedGroups = groups.Where(x => x.Value == 0).Select(x => x.Key).ToList();
            ret.Children.AddRange(assignedGroups); // Add the groups with no parent
            while (groups.Any())
            {
                var canAssign = groups.Where(x => assignedGroups.Any(y => y.ID == x.Value)).ToList();
                if (!canAssign.Any()) break;
                foreach (var kv in canAssign)
                {
                    // Add the group to the tree and the assigned list, remove it from the groups list
                    var parent = assignedGroups.First(y => y.ID == kv.Value);
                    kv.Key.Parent = parent;
                    parent.Children.Add(kv.Key);
                    assignedGroups.Add(kv.Key);
                    groups.Remove(kv.Key);
                }
            }

            // Load visible solids
            foreach (var solid in world.GetChildren("solid"))
            {
                var s = ReadSolid(solid, generator);
                if (s == null) continue;

                var editor = solid.GetChildren("editor").FirstOrDefault() ?? new GenericStructure("editor");
                var gid = editor.PropertyLong("groupid");
                var parent = gid > 0 ? assignedGroups.FirstOrDefault(x => x.ID == gid) ?? (MapObject) ret : ret;
                s.Parent = parent;
                parent.Children.Add(s);
                parent.UpdateBoundingBox();
            }

            // Load hidden solids
            foreach (var hidden in world.GetChildren("hidden"))
            {
                foreach (var solid in hidden.GetChildren("solid"))
                {
                    var s = ReadSolid(solid, generator);
                    if (s == null) continue;

                    s.IsVisgroupHidden = true;

                    var editor = solid.GetChildren("editor").FirstOrDefault() ?? new GenericStructure("editor");
                    var gid = editor.PropertyLong("groupid");
                    var parent = gid > 0 ? assignedGroups.FirstOrDefault(x => x.ID == gid) ?? (MapObject)ret : ret;
                    s.Parent = parent;
                    parent.Children.Add(s);
                    parent.UpdateBoundingBox();
                }
            }

            assignedGroups.ForEach(x => x.UpdateBoundingBox(false));

            return ret;
        }

        private static GenericStructure WriteWorld(DataStructures.MapObjects.Map map, IEnumerable<Solid> solids, IEnumerable<Group> groups)
        {
            var world = map.WorldSpawn;
            var ret = new GenericStructure("world");
            ret["id"] = world.ID.ToString();
            ret["classname"] = "worldspawn";
            WriteEntityData(ret, world.EntityData);

            //TODO these properties
            ret["mapversion"] = map.Version.ToString();
            ret["detailmaterial"] = "detail/detailsprites";
            ret["detailvbsp"] = "detail.vbsp";
            ret["maxpropscreenwidth"] = "-1";
            ret["skyname"] = "sky_day01_01";

            foreach (var solid in solids)
            {
                ret.Children.Add(WriteSolid(solid));
            }

            foreach (var group in groups)
            {
                ret.Children.Add(WriteGroup(group));
            }

            return ret;
        }

        private static Visgroup ReadVisgroup(GenericStructure visgroup)
        {
            var v = new Visgroup
                        {
                            Name = visgroup["name"],
                            ID = visgroup.PropertyInteger("visgroupid"),
                            Colour = visgroup.PropertyColour("color", Colour.GetRandomBrushColour()),
                            Visible = true
                        };
            return v;
        }

        private static GenericStructure WriteVisgroup(Visgroup visgroup)
        {
            var ret = new GenericStructure("visgroup");
            ret["name"] = visgroup.Name;
            ret["visgroupid"] = visgroup.ID.ToString();
            ret["color"] = FormatColor(visgroup.Colour);
            return ret;
        }

        public static GenericStructure CreateCopyStream(List<MapObject> objects)
        {
            var stream = new GenericStructure("clipboard");

            stream.Children.AddRange(objects.OfType<Solid>().Where(x => !x.IsCodeHidden && !x.IsVisgroupHidden).Select(WriteSolid));
            stream.Children.AddRange(objects.OfType<Group>().Select(WriteGroup));
            stream.Children.AddRange(objects.OfType<Entity>().Select(WriteEntity));

            return stream;
        }

        public static IEnumerable<MapObject> ExtractCopyStream(GenericStructure gs, IDGenerator generator)
        {
            if (gs == null || gs.Name != "clipboard") return null;
            var dummyGen = new IDGenerator();
            var list = new List<MapObject>();
            list.AddRange(ReadWorld(gs, dummyGen).Children);
            list.AddRange(gs.GetChildren("entity").Select(x => ReadEntity(x, dummyGen)));
            Reindex(list, generator);
            return list;
        }

        private static void Reindex(IEnumerable<MapObject> objs, IDGenerator generator)
        {
            foreach (var o in objs)
            {
                if (o is Solid) ((Solid) o).Faces.ForEach(x => x.ID = generator.GetNextFaceID());
                o.ID = generator.GetNextObjectID();
                if (o.Children.Count == 0) o.UpdateBoundingBox();
                Reindex(o.Children, generator);
            }
        }

        protected override DataStructures.MapObjects.Map GetFromStream(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                var parent = new GenericStructure("Root");
                parent.Children.AddRange(GenericStructure.Parse(reader));
                // Sections from a Hammer map:
                // versioninfo
                // visgroups
                // viewsettings
                // world
                // entity
                // cameras
                // cordon
                // (why does hammer save these in reverse alphabetical?)

                var map = new DataStructures.MapObjects.Map();

                var world = parent.GetChildren("world").FirstOrDefault();
                var entities = parent.GetChildren("entity");
                var visgroups = parent.GetChildren("visgroups").SelectMany(x => x.GetChildren("visgroup"));
                var cameras = parent.GetChildren("cameras").FirstOrDefault();

                foreach (var visgroup in visgroups)
                {
                    map.Visgroups.Add(ReadVisgroup(visgroup));
                }

                if (world != null) map.WorldSpawn = ReadWorld(world, map.IDGenerator);
                foreach (var entity in entities)
                {
                    var ent = ReadEntity(entity, map.IDGenerator);
                    ent.Parent = map.WorldSpawn;
                    map.WorldSpawn.Children.Add(ent);
                }

                return map;
            }
        }

        protected override void SaveToStream(Stream stream, DataStructures.MapObjects.Map map)
        {
            var groups = new List<Group>();
            var solids = new List<Solid>();
            var ents = new List<Entity>();
            FlattenTree(map.WorldSpawn, solids, ents, groups);

            var versioninfo = new GenericStructure("versioninfo");
            //TODO versioninfo

            var visgroups = new GenericStructure("visgroups");
            foreach (var visgroup in map.Visgroups)
            {
                visgroups.Children.Add(WriteVisgroup(visgroup));
            }

            var viewsettings = new GenericStructure("viewsettings");
            //TODO viewsettings

            var world = WriteWorld(map, solids, groups);

            var entities = ents.Select(WriteEntity).ToList();

            var cameras = new GenericStructure("cameras");
            //TODO cameras

            var cordon = new GenericStructure("cordon");
            //TODO cordon

            using (var sw = new StreamWriter(stream))
            {
                sw.Write(versioninfo);
                sw.Write(visgroups);
                sw.Write(viewsettings);
                sw.Write(world);
                entities.ForEach(sw.Write);
                sw.Write(cameras);
                sw.Write(cordon);
            }
        }
    }
}