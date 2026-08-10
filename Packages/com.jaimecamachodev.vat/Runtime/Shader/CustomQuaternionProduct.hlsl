float3 RotatePositionByQuaternion_float(float3 position, float4 rotation, out float3 rotatedPosition)
{
    float3 q_xyz = rotation.xyz;
    float q_w = rotation.w;
    
    float3 t = 2.0 * cross(q_xyz, position);
    return rotatedPosition = position + q_w * t + cross(q_xyz, t);
}
