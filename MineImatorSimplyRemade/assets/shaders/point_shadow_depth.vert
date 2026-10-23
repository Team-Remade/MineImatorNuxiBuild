#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in ivec4 aBoneIndices;
layout (location = 4) in vec4 aBoneWeights;

uniform mat4  uLightViewProj;
uniform mat4  uModel;
uniform vec2  uTexOffset;
uniform float uTexScaleV;
uniform vec2  uTexUvOffset;
uniform vec2  uTexUvRepeat;
uniform vec2  uTexUvMirror;
uniform bool  uIsSkinned;
uniform mat4  uBoneMatrices[64];
uniform bool uBendEnabled; uniform vec3 uBendAngle; uniform vec3 uBendPivot; uniform vec3 uBendShapePosition;
uniform mat4 uBendInverseShape; uniform int uBendSegmentAxis; uniform int uBendScaleAxis;
uniform float uBendAxisStart; uniform float uBendStart; uniform float uBendEnd; uniform float uBendSize;
uniform bool uBendInvert; uniform bool uBendSharp;
uniform bool uBendPlane;

out vec3 vWorldPos;
out vec2 vTexCoord;

float bc(vec3 v,int a){return a==0?v.x:a==1?v.y:v.z;} float be(float t){if(t<=0.0)return 0.0;if(t>=1.0)return 1.0;float x=t*2.0;return x<1.0?0.5*pow(x,5.0):1.0-0.5*pow(2.0-x,5.0);} mat3 br(vec3 d){vec3 a=radians(d);float cx=cos(a.x),sx=sin(a.x),cy=cos(a.y),sy=sin(a.y),cz=cos(a.z),sz=sin(a.z);return mat3(cy,0,-sy,0,1,0,sy,0,cy)*mat3(1,0,0,0,cx,sx,0,-sx,cx)*mat3(cz,sz,0,-sz,cz,0,0,0,1);}
vec3 bendPosition(vec3 p){if(!uBendEnabled||all(equal(uBendAngle,vec3(0.0))))return p;vec3 l=p-uBendShapePosition;vec3 src=(uBendInverseShape*vec4(l,1.0)).xyz;float z=bc(src,uBendSegmentAxis)-uBendAxisStart;float w=z<uBendStart?0.0:z>=uBendEnd?1.0:1.0-(uBendEnd-z)/uBendSize;if(uBendInvert)w=1.0-w;bool initial=abs(z)<=max(1e-7,abs(uBendAxisStart)*1e-6);vec3 v=uBendSharp?uBendAngle*w:vec3(uBendAngle.x*be(w),uBendAngle.y*w,uBendAngle.z*be(w));vec3 s=vec3(1.0+(!uBendPlane&&!initial?w*0.005:0.0));if(uBendSharp&&(!uBendPlane||initial)&&uBendScaleAxis>=0&&z>uBendStart&&z<uBendEnd){float q=(w<=0.5?w:1.0-w)*2.0;float a=abs(bc(uBendAngle,uBendScaleAxis));if(a>90.0)a=180.0-a;float c=pow(q*clamp(a/90.0,0.0,1.0),3.0)/2.5;if(uBendScaleAxis!=0)s.x+=c;if(uBendScaleAxis!=1)s.y+=c;if(uBendScaleAxis!=2)s.z+=c;}return uBendPivot+br(v)*(s*(l-uBendPivot))+uBendShapePosition;}

vec2 applyUvTransform(vec2 uv)
{
    vec2 repeated = uv * uTexUvRepeat;

    if (uTexUvMirror.x > 0.5)
        repeated.x = abs(fract(repeated.x * 0.5) * 2.0 - 1.0);

    if (uTexUvMirror.y > 0.5)
        repeated.y = abs(fract(repeated.y * 0.5) * 2.0 - 1.0);

    return repeated + uTexUvOffset;
}

void main() {
    vec3 bentPosition = bendPosition(aPos);
    vec4 pos;
    if (uIsSkinned) {
        mat4 skinMatrix = mat4(0.0);
        for (int i = 0; i < 4; i++) {
            if (aBoneIndices[i] >= 0 && aBoneIndices[i] < 64 && aBoneWeights[i] > 0.0) {
                skinMatrix += aBoneWeights[i] * uBoneMatrices[aBoneIndices[i]];
            }
        }
        pos = skinMatrix * vec4(bentPosition, 1.0);
    } else {
        pos = vec4(bentPosition, 1.0);
    }

    vec4 worldPos = uModel * pos;
    vWorldPos = worldPos.xyz;
    vec2 baseUv = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vTexCoord = applyUvTransform(baseUv);
    gl_Position = uLightViewProj * worldPos;
}
