"""Game mesh from a TRELLIS.2 GLB: geometry-only quadric edge collapse in MeshLab (pymeshlab), which copes with
TRELLIS's thin double walls and non-manifold seams where Blender's decimator shreds the surface. The texture is not
kept: Tools/Blender/zcom_props.py re-bakes the albedo from the GLB onto the result. The result is unwrapped with
xatlas (large charts; Blender's smart project cuts a simplified scan into thousands of slivers).

    python -I trellis_decimate.py <in.glb> <out.obj> <target_faces>     (needs: pip install pymeshlab trimesh xatlas)
"""
import sys
import time
from pathlib import Path

import pymeshlab
import trimesh
import xatlas

src, dst, target = sys.argv[1], Path(sys.argv[2]), int(sys.argv[3])
dst.parent.mkdir(parents=True, exist_ok=True)
t = time.time()
scene = trimesh.load(src)
mesh = scene.to_geometry() if hasattr(scene, "to_geometry") else scene
tmp = dst.with_name(dst.stem + "_src.obj")
mesh.export(tmp)
print("exported source obj", len(mesh.faces), f"{time.time() - t:.0f}s", flush=True)

ms = pymeshlab.MeshSet()
ms.load_new_mesh(str(tmp))
m = ms.current_mesh()
print("loaded", m.vertex_number(), "verts", m.face_number(), "faces, wedge tex", m.has_wedge_tex_coord(), flush=True)
ms.meshing_merge_close_vertices(threshold=pymeshlab.PercentageValue(0.01))
ms.meshing_remove_duplicate_faces()
ms.meshing_remove_null_faces()
print("cleaned", ms.current_mesh().vertex_number(), "verts", flush=True)
ms.meshing_decimation_quadric_edge_collapse(
    targetfacenum=target, qualitythr=0.5, preserveboundary=False, preservenormal=True, preservetopology=False,
    optimalplacement=True, planarquadric=True, autoclean=True)
m = ms.current_mesh()
print("decimated", m.vertex_number(), "verts", m.face_number(), "faces", f"{time.time() - t:.0f}s", flush=True)
raw = dst.with_name(dst.stem + "_raw.obj")
ms.save_current_mesh(str(raw), save_wedge_texcoord=False, save_textures=False, save_vertex_color=False)
lo = trimesh.load(raw, process=False)
atlas = xatlas.Atlas()
atlas.add_mesh(lo.vertices, lo.faces)
co = xatlas.ChartOptions(); co.max_iterations = 4
po = xatlas.PackOptions(); po.padding = 4; po.resolution = 2048; po.bilinear = True
atlas.generate(co, po)
vmap, faces, uvs = atlas[0]
xatlas.export(str(dst), lo.vertices[vmap], faces, uvs)
print("unwrapped", atlas.chart_count, "charts", f"{time.time() - t:.0f}s")
print("saved", dst)
