import cv2, numpy as np, os
BASE = os.path.dirname(os.path.abspath(__file__))
g = cv2.imread(os.path.join(BASE, "test_plate.jpg"), cv2.IMREAD_GRAYSCALE)

_, b = cv2.threshold(g, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
b = cv2.morphologyEx(b, cv2.MORPH_CLOSE, np.ones((7,7), np.uint8))
b = cv2.morphologyEx(b, cv2.MORPH_OPEN,  np.ones((7,7), np.uint8))
cnts, _ = cv2.findContours(b, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
c = max(cnts, key=cv2.contourArea)
ap = cv2.approxPolyDP(c, 0.02*cv2.arcLength(c, True), True).reshape(-1,2).astype("float32")

s = ap.sum(1); d = np.diff(ap, axis=1).ravel()
src = np.array([ap[np.argmin(s)], ap[np.argmin(d)],
                ap[np.argmax(s)], ap[np.argmax(d)]], dtype="float32")
tl, tr, br, bl = src
W = int(max(np.linalg.norm(tr-tl), np.linalg.norm(br-bl)))
H = int(max(np.linalg.norm(bl-tl), np.linalg.norm(br-tr)))
dst = np.array([[0,0],[W-1,0],[W-1,H-1],[0,H-1]], dtype="float32")
warp = cv2.warpPerspective(g, cv2.getPerspectiveTransform(src, dst), (W, H))

cv2.imwrite(os.path.join(BASE, "plate_deskewed.jpg"), warp)