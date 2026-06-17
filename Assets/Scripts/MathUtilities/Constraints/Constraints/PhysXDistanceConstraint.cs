using UnityEngine;
public class VelocityConstraint : MonoBehaviour {
  Rigidbody body;
  public Rigidbody otherBody;
  public float Dist = 1f;
  void Start() {
    body = GetComponent<Rigidbody>();
  }
  void FixedUpdate() {
    ConstrainRigidbodies(body, otherBody, Dist * Dist);
  }
  void ConstrainRigidbodies(Rigidbody body, Rigidbody otherBody, float sqrDist) {
    Vector3 offset = (otherBody.position + otherBody.linearVelocity * Time.fixedDeltaTime) - (body.position + body.linearVelocity * Time.fixedDeltaTime);
    if ((offset).sqrMagnitude > sqrDist) {
      offset *= sqrDist / (offset.sqrMagnitude + sqrDist) - 0.5f;
      body.linearVelocity -= offset / Time.fixedDeltaTime;
      otherBody.linearVelocity += offset / Time.fixedDeltaTime;
    }
  }
}
