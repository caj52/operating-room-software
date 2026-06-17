using UnityEngine;

public class AccelerationDamping : MonoBehaviour {
  public float dampingRatio = 0.5f;
  Vector3 position, velocity, angularVelocity;
  Quaternion rotation = Quaternion.identity;
  Rigidbody body;
  bool updateHappened = false;

  void Start() {
    body = GetComponent<Rigidbody>();
  }

	void FixedUpdate () {
    if (!updateHappened) {
      applyDamping();
    }

    updateHappened = false;
  }
	
	void Update () {
    applyDamping();
    updateHappened = true;
	}


  void applyDamping() {
    body.position = Vector3.Lerp(body.position, position + (Time.fixedDeltaTime * velocity), dampingRatio);
    transform.position = body.position;
    position = body.position;
    body.linearVelocity = Vector3.Lerp(body.linearVelocity, velocity /*+ (Physics.gravity * Mathf.Pow(Time.fixedDeltaTime, 2f) * 2f)*/, dampingRatio);
    velocity = body.linearVelocity;


    body.rotation = Quaternion.Slerp(body.rotation, Quaternion.Euler(angularVelocity*Time.fixedDeltaTime)*rotation, dampingRatio);
    transform.rotation = body.rotation;
    rotation = body.rotation;
    body.angularVelocity = Vector3.Lerp(body.angularVelocity, angularVelocity, dampingRatio);
    angularVelocity = body.angularVelocity;
  }
}