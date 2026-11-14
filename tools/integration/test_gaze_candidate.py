"""Synthetic geometry and mapping checks; no accuracy claim for real gaze."""
import unittest
import cv2
import numpy as np
from solve_gaze_candidate import gaze_directions,predicted,fit_multistart,camera_to_screen,quaternion

class GeometryChecks(unittest.TestCase):
    def test_upstream_axes(self):
        np.testing.assert_allclose(gaze_directions([[0,0],[90,0],[0,90]]),[[0,0,-1],[-1,0,0],[0,1,0]],atol=1e-12)

    def test_moving_eyes_and_mapping(self):
        rng = np.random.default_rng(7)
        targets = np.array([[x,y,0] for x in [-.2,0,.2] for y in [-.12,0,.12] for _ in range(8)])
        eyes = rng.normal([.04,.02,.6],[.035,.02,.04],targets.shape)
        truth = np.array([.12,2.9,-.08,.2,.05,.02])
        gaze = predicted(truth,targets,eyes,1.)
        recovered,_ = fit_multistart(targets,eyes,gaze,np.ones(len(eyes)),1.)
        np.testing.assert_allclose(predicted(recovered,targets,eyes,1.),gaze,atol=1e-6)
        rotation = cv2.Rodrigues(recovered[:3])[0]
        q0 = eyes.mean(axis=0); e0 = camera_to_screen(recovered,q0,1.)
        q = quaternion(rotation.T); v = eyes-q0
        translated = e0+v+2*np.cross(q[:3],np.cross(q[:3],v)+q[3]*v)
        np.testing.assert_allclose(translated,camera_to_screen(recovered,eyes,1.),atol=1e-10)

if __name__=='__main__': unittest.main()
