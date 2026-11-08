<?xml version="1.0" encoding="utf-8"?>
<!--
  Fixture przygotowany ręcznie (nie pochodzi z usługi): GML 3.1 z gml:featureMember,
  gml:Surface z łukiem (Arc) i odcinkiem, współrzędne 3D w kolejności północ, wschód (urn EPSG:2180).
  Kształt: półkole o środku E=508050, N=327000 i promieniu 50 m, zamknięte średnicą.
-->
<gml:FeatureCollection xmlns:gml="http://www.opengis.net/gml" xmlns:test="urn:geobench:test">
  <gml:featureMember>
    <test:Polkole gml:id="polkole.1">
      <test:nazwa>półkole</test:nazwa>
      <test:geometria>
        <gml:Surface srsName="urn:ogc:def:crs:EPSG::2180" srsDimension="3">
          <gml:patches>
            <gml:PolygonPatch>
              <gml:exterior>
                <gml:Ring>
                  <gml:curveMember>
                    <gml:Curve>
                      <gml:segments>
                        <gml:Arc>
                          <gml:posList>327000 508000 0 327050 508050 0 327000 508100 0</gml:posList>
                        </gml:Arc>
                        <gml:LineStringSegment>
                          <gml:posList>327000 508100 0 327000 508000 0</gml:posList>
                        </gml:LineStringSegment>
                      </gml:segments>
                    </gml:Curve>
                  </gml:curveMember>
                </gml:Ring>
              </gml:exterior>
            </gml:PolygonPatch>
          </gml:patches>
        </gml:Surface>
      </test:geometria>
    </test:Polkole>
  </gml:featureMember>
</gml:FeatureCollection>
